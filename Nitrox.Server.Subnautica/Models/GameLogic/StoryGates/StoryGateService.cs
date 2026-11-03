using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;

/// <summary>
///     Story gates (COOP_FEATURES_SPEC G0): a major story moment starts only when everyone on the roster is there. A game
///     that holds a moment back sends <see cref="StoryGateReady" />; this service answers everyone with a
///     <see cref="StoryGateStatus" /> while it waits and a <see cref="StoryGateStart" /> once the gate's rule
///     (<see cref="StoryGateRules" />) is met. With <see cref="SubnauticaServerOptions.StoryGatesEnabled" /> off every
///     ready starts at once.
/// </summary>
internal sealed class StoryGateService(
    IPacketSender packetSender,
    PlayerManager playerManager,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<StoryGateService> logger)
    : ISessionCleaner
{
    private static readonly NitroxTechType rocketTechType = new("RocketBase");

    private readonly IPacketSender packetSender = packetSender;
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<StoryGateService> logger = logger;
    private readonly StoryGateState state = new();
    private readonly Dictionary<StoryGateKind, StoryGateDecision> lastStatus = [];
    private readonly HashSet<StoryGateKind> started = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();

    /// <summary>The players the gates wait for, from the option or else every saved player.</summary>
    public string[] Roster => StoryGateState.ParseRoster(options.Value.StoryGatePlayers, playerManager.GetAllPlayers().Select(player => player.Name));

    public async Task SetReadyAsync(Player player, StoryGateKind kind, bool ready)
    {
        if (!Enum.IsDefined(kind))
        {
            return;
        }
        if (!options.Value.StoryGatesEnabled)
        {
            if (ready)
            {
                lock (state)
                {
                    started.Add(kind);
                }
                await packetSender.SendPacketToAllAsync(new StoryGateStart(kind));
            }
            return;
        }

        StoryGateDecision decision;
        lock (state)
        {
            state.SetReady(kind, player.Name, ready, clock.Elapsed);
            decision = Evaluate(kind, OnlinePlayers(playerManager.GetConnectedPlayers()));
        }
        // A renewal that changes nothing goes unanswered, so a welder's renewals don't flood everyone with statuses.
        await SendAsync(kind, decision, null);
    }

    /// <summary>
    ///     Admin escape hatch: the gate's moment starts now if someone is waiting for it, else the next time someone asks,
    ///     without waiting for the others.
    /// </summary>
    /// <returns>True if it started at once.</returns>
    public async Task<bool> ReleaseAsync(StoryGateKind kind)
    {
        StoryGateDecision decision;
        lock (state)
        {
            state.Release(kind);
            decision = state.HasReady(kind) ? Evaluate(kind, OnlinePlayers(playerManager.GetConnectedPlayers())) : StoryGateDecision.None;
        }
        await SendAsync(kind, decision, null);
        return decision.Outcome == StoryGateOutcome.START;
    }

    /// <summary>
    ///     True once the gate's moment has started since the server started (or the gates are off). The rocket launch is
    ///     only relayed after that (G2), since a launch can't be taken back.
    /// </summary>
    public bool HasStarted(StoryGateKind kind)
    {
        if (!options.Value.StoryGatesEnabled)
        {
            return true;
        }
        lock (state)
        {
            return started.Contains(kind);
        }
    }

    /// <summary>One line per gate for the <c>storygate status</c> command.</summary>
    public List<string> DescribeGates()
    {
        List<StoryGatePlayer> online = OnlinePlayers(playerManager.GetConnectedPlayers());
        List<string> lines = [$"Story gates are {(options.Value.StoryGatesEnabled ? "on" : "off")}. Roster: {string.Join(", ", Roster)}. Online: {string.Join(", ", online.Select(player => player.Name))}"];
        lock (state)
        {
            foreach (StoryGateKind kind in Enum.GetValues<StoryGateKind>())
            {
                StoryGateDefinition gate = Definition(kind);
                string line = $"{kind}: {gate.Rule}{(gate.Area == null ? "" : $" in the {gate.Area.Name}")}";
                if (state.IsReleased(kind))
                {
                    line += ", released for the next start";
                }
                if (state.HasReady(kind) && lastStatus.TryGetValue(kind, out StoryGateDecision? status))
                {
                    line += $", {status.Outcome}: ready {string.Join(", ", status.ReadyNames)}, waiting for {string.Join(", ", status.MissingNames)}";
                }
                lines.Add(line);
            }
        }
        return lines;
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        // The session cleaners run one after another in no fixed order, so PlayerManager may still list the leaving player.
        List<Player> remaining = playerManager.GetConnectedPlayersExcept(args.Session.Id);
        string? leaverName = playerManager.TryGetPlayerBySessionId(args.Session.Id, out Player? leaver)
                                 ? leaver.Name
                                 : playerManager.GetAllPlayers().FirstOrDefault(player => player.SessionId == args.Session.Id)?.Name;
        List<(StoryGateKind kind, StoryGateDecision decision)> decisions = [];
        lock (state)
        {
            if (leaverName != null)
            {
                state.RemovePlayer(leaverName);
            }
            List<StoryGatePlayer> online = OnlinePlayers(remaining);
            foreach (StoryGateKind kind in state.KindsWithReady())
            {
                decisions.Add((kind, Evaluate(kind, online)));
            }
        }
        foreach ((StoryGateKind kind, StoryGateDecision decision) in decisions)
        {
            await SendAsync(kind, decision, args.Session.Id);
        }
    }

    private StoryGateDecision Evaluate(StoryGateKind kind, List<StoryGatePlayer> online) =>
        state.Evaluate(Definition(kind), Roster, online, clock.Elapsed);

    /// <summary>The gate's rule, with the area of a rocket's cockpit for the rocket launch (it stands where it was built).</summary>
    private StoryGateDefinition Definition(StoryGateKind kind)
    {
        StoryGateDefinition gate = StoryGateRules.For(kind);
        if (kind == StoryGateKind.ROCKET_LAUNCH && gate.Area == null && FindRocket() is { } rocket)
        {
            gate = gate with { Area = StoryGateRules.RocketCockpit(rocket.Transform.Position, rocket.Transform.Rotation) };
        }
        return gate;
    }

    /// <summary>
    ///     The rocket the launch is about: the most built one (H: a world with two rockets uses the one with more stages).
    ///     Null if none is built; the launch then waits for everyone's click without an area.
    /// </summary>
    private VehicleEntity? FindRocket() =>
        entityRegistry.GetEntities<VehicleEntity>()
                      .Where(vehicle => rocketTechType.Equals(vehicle.TechType))
                      .OrderByDescending(vehicle => vehicle.Metadata is RocketMetadata rocketMetadata ? rocketMetadata.CurrentStage : -1)
                      .FirstOrDefault();

    private static List<StoryGatePlayer> OnlinePlayers(List<Player> players) => players.Select(player => new StoryGatePlayer(player.Name, player.Position)).ToList();

    /// <summary>Sends a start, or a status if it differs from the last one sent for this gate.</summary>
    /// <param name="exceptSession">A session that is leaving and mustn't be sent to.</param>
    private async Task SendAsync(StoryGateKind kind, StoryGateDecision decision, SessionId? exceptSession)
    {
        bool changed;
        lock (state)
        {
            lastStatus.TryGetValue(kind, out StoryGateDecision? previous);
            changed = !decision.SameStatusAs(previous);
            if (decision.Outcome is StoryGateOutcome.NONE or StoryGateOutcome.START)
            {
                lastStatus.Remove(kind);
            }
            if (decision.Outcome == StoryGateOutcome.START)
            {
                started.Add(kind);
            }
            else
            {
                lastStatus[kind] = decision;
            }
        }

        switch (decision.Outcome)
        {
            case StoryGateOutcome.START:
                logger.ZLogInformation($"Story gate {kind} starts for {string.Join(", ", decision.ReadyNames)}");
                await SendToAllAsync(new StoryGateStart(kind), exceptSession);
                break;
            case StoryGateOutcome.LOCKED or StoryGateOutcome.WAITING when changed:
                logger.ZLogInformation($"Story gate {kind} {decision.Outcome}: ready {string.Join(", ", decision.ReadyNames)}, waiting for {string.Join(", ", decision.MissingNames)}");
                await SendToAllAsync(new StoryGateStatus(kind, decision.ReadyNames, decision.MissingNames, decision.Outcome == StoryGateOutcome.LOCKED), exceptSession);
                break;
        }
    }

    private async Task SendToAllAsync<T>(T packet, SessionId? exceptSession) where T : Packet
    {
        if (exceptSession.HasValue)
        {
            await packetSender.SendPacketToOthersAsync(packet, exceptSession.Value);
        }
        else
        {
            await packetSender.SendPacketToAllAsync(packet);
        }
    }
}
