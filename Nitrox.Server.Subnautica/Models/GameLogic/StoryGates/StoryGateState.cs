using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;

/// <summary>An online player as the gates see them: the name and the position the game last reported.</summary>
internal readonly record struct StoryGatePlayer(string Name, NitroxVector3 Position);

internal enum StoryGateOutcome
{
    /// <summary>Nobody is waiting for this gate.</summary>
    NONE,

    /// <summary>Someone on the roster is offline.</summary>
    LOCKED,

    /// <summary>Everyone is online, but someone hasn't confirmed or isn't there yet.</summary>
    WAITING,

    /// <summary>The moment may start. The ready players were cleared.</summary>
    START
}

/// <param name="ReadyNames">Who asked for the moment (sorted).</param>
/// <param name="MissingNames">If locked: the roster players who are offline; else the ones still missing (sorted).</param>
internal sealed record StoryGateDecision(StoryGateOutcome Outcome, string[] ReadyNames, string[] MissingNames)
{
    public static readonly StoryGateDecision None = new(StoryGateOutcome.NONE, [], []);

    public bool SameStatusAs(StoryGateDecision? other) =>
        other != null && Outcome == other.Outcome && ReadyNames.SequenceEqual(other.ReadyNames) && MissingNames.SequenceEqual(other.MissingNames);
}

/// <summary>
///     The gates' state machine without networking (COOP_FEATURES_SPEC G0): who is ready for which gate since when, and
///     what a gate decides for a roster and the players online. Not thread-safe; <see cref="StoryGateService" /> locks it.
/// </summary>
internal sealed class StoryGateState
{
    private static readonly StringComparer nameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<StoryGateKind, Dictionary<string, TimeSpan>> readySince = [];
    private readonly HashSet<StoryGateKind> released = [];

    /// <summary>
    ///     The players gates wait for: the names in <paramref name="option" /> (comma-separated, trimmed, without duplicates)
    ///     or, if it has none, <paramref name="savedPlayerNames" />.
    /// </summary>
    public static string[] ParseRoster(string? option, IEnumerable<string> savedPlayerNames)
    {
        string[] fromOption = (option ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                            .Distinct(nameComparer)
                                            .ToArray();
        if (fromOption.Length > 0)
        {
            return fromOption;
        }
        return savedPlayerNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(nameComparer).ToArray();
    }

    public bool HasReady(StoryGateKind kind) => readySince.TryGetValue(kind, out Dictionary<string, TimeSpan>? ready) && ready.Count > 0;

    public bool IsReleased(StoryGateKind kind) => released.Contains(kind);

    public IEnumerable<StoryGateKind> KindsWithReady() => readySince.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key).ToArray();

    /// <summary>Records or renews (<paramref name="ready" /> true) or withdraws a player's ready.</summary>
    public void SetReady(StoryGateKind kind, string playerName, bool ready, TimeSpan now)
    {
        if (!readySince.TryGetValue(kind, out Dictionary<string, TimeSpan>? players))
        {
            players = new Dictionary<string, TimeSpan>(nameComparer);
            readySince[kind] = players;
        }
        if (ready)
        {
            players[playerName] = now;
        }
        else
        {
            players.Remove(playerName);
        }
    }

    /// <summary>Clears the player's ready for every gate (they left the game).</summary>
    /// <returns>The gates the player was ready for.</returns>
    public List<StoryGateKind> RemovePlayer(string playerName)
    {
        List<StoryGateKind> kinds = [];
        foreach ((StoryGateKind kind, Dictionary<string, TimeSpan> players) in readySince)
        {
            if (players.Remove(playerName))
            {
                kinds.Add(kind);
            }
        }
        return kinds;
    }

    /// <summary>
    ///     Lets the gate's next start through without waiting (admin escape hatch). It's used up by that start.
    /// </summary>
    public void Release(StoryGateKind kind) => released.Add(kind);

    /// <summary>
    ///     Drops expired readies and readies of players who left the gate's area, then decides. On
    ///     <see cref="StoryGateOutcome.START" /> the gate's readies and release are cleared, so the next attempt waits again.
    /// </summary>
    public StoryGateDecision Evaluate(StoryGateDefinition gate, IReadOnlyCollection<string> roster, IReadOnlyCollection<StoryGatePlayer> online, TimeSpan now)
    {
        if (!readySince.TryGetValue(gate.Kind, out Dictionary<string, TimeSpan>? ready))
        {
            return StoryGateDecision.None;
        }
        Dictionary<string, StoryGatePlayer> onlineByName = online.GroupBy(player => player.Name, nameComparer)
                                                                .ToDictionary(group => group.Key, group => group.First(), nameComparer);
        foreach (string name in ready.Keys.ToArray())
        {
            bool expired = now - ready[name] > gate.ReadyTimeout;
            bool offline = !onlineByName.TryGetValue(name, out StoryGatePlayer player);
            bool leftArea = gate.Area != null && !offline && !gate.Area.Contains(player.Position);
            if (expired || offline || leftArea)
            {
                ready.Remove(name);
            }
        }
        if (ready.Count == 0)
        {
            return StoryGateDecision.None;
        }

        string[] readyNames = Sorted(ready.Keys);
        string[] offlineNames = Sorted(roster.Where(name => !onlineByName.ContainsKey(name)));
        if (released.Contains(gate.Kind))
        {
            return Start(gate.Kind, readyNames);
        }
        if (offlineNames.Length > 0)
        {
            return new StoryGateDecision(StoryGateOutcome.LOCKED, readyNames, offlineNames);
        }

        string[] missingNames = gate.Rule switch
        {
            StoryGateRule.ONLINE_ONLY => [],
            StoryGateRule.ALL_READY => Sorted(roster.Where(name => !ready.ContainsKey(name))),
            StoryGateRule.ONE_READY_ALL_PRESENT => Sorted(roster.Where(name => gate.Area != null && !gate.Area.Contains(onlineByName[name].Position))),
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate.Rule, null)
        };
        if (missingNames.Length > 0)
        {
            return new StoryGateDecision(StoryGateOutcome.WAITING, readyNames, missingNames);
        }
        return Start(gate.Kind, readyNames);
    }

    private StoryGateDecision Start(StoryGateKind kind, string[] readyNames)
    {
        readySince.Remove(kind);
        released.Remove(kind);
        return new StoryGateDecision(StoryGateOutcome.START, readyNames, []);
    }

    private static string[] Sorted(IEnumerable<string> names) => names.OrderBy(name => name, nameComparer).ToArray();
}
