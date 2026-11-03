using System;

namespace Nitrox.Model.Subnautica.Helper;

/// <summary>
///     Which Precursor teleporter pairs are active travels as one story goal per teleporter identifier (MULTINAUGHTICA_SPEC M2).
///     The server saves goals and sends them to every joiner; the game's own list (<c>TeleporterManager.activeTeleporters</c>)
///     is part of the single-player save that Nitrox games don't use. Without it, the far end of an activated pair stayed
///     dark after a restart for a player who arrives there first. The prefix can't collide with a game goal: no goal key in
///     the game's code starts with "Nitrox", and the M2 scenario checks the goal data loaded at runtime.
/// </summary>
public static class TeleporterGoalKey
{
    public const string PREFIX = "Nitrox_TeleporterActive:";

    public static string Format(string identifier) => PREFIX + identifier;

    public static bool TryParse(string? key, out string? identifier)
    {
        if (key != null && key.Length > PREFIX.Length && key.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            identifier = key.Substring(PREFIX.Length);
            return true;
        }
        identifier = null;
        return false;
    }
}
