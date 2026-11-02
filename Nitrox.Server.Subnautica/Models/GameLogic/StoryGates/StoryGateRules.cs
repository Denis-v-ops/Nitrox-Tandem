using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;

/// <summary>
///     What "everyone is there" means for a story gate (COOP_FEATURES_SPEC G0). Every rule first needs everyone on the
///     roster online; while someone is offline the gate is locked.
/// </summary>
internal enum StoryGateRule
{
    /// <summary>A lock only: the moment starts as soon as one player asks while everyone on the roster is online.</summary>
    ONLINE_ONLY,

    /// <summary>Every player on the roster has to ask (click) for it within <see cref="StoryGateDefinition.ReadyTimeout" />.</summary>
    ALL_READY,

    /// <summary>One player asks, and every player on the roster has to be inside <see cref="StoryGateDefinition.Area" />.</summary>
    ONE_READY_ALL_PRESENT
}

/// <summary>An axis-aligned box in world coordinates, checked against the positions players last reported.</summary>
internal sealed record StoryGateArea(string Name, NitroxVector3 Min, NitroxVector3 Max)
{
    public bool Contains(NitroxVector3 position) =>
        position.X >= Min.X && position.X <= Max.X &&
        position.Y >= Min.Y && position.Y <= Max.Y &&
        position.Z >= Min.Z && position.Z <= Max.Z;
}

/// <param name="ReadyTimeout">
///     A ready expires this long after the player last sent it. Games that hold an ongoing attempt (welding) renew it
///     every second.
/// </param>
/// <param name="Area">
///     Where the moment happens. With <see cref="StoryGateRule.ALL_READY" /> a ready only counts while its player is inside;
///     leaving clears it. With <see cref="StoryGateRule.ONE_READY_ALL_PRESENT" /> everyone on the roster must be inside.
/// </param>
internal sealed record StoryGateDefinition(StoryGateKind Kind, StoryGateRule Rule, TimeSpan ReadyTimeout, StoryGateArea? Area = null);

/// <summary>
///     The rule of each gate. G1–G3 change their own line here when they hook their moment (e.g. G2 adds the rocket's
///     area so that "ready" means "aboard").
/// </summary>
internal static class StoryGateRules
{
    /// <summary>
    ///     The Aurora's drive core room, around the 11 radiation leaks. Measured in game (windowless, 2026-11-02): see the
    ///     G4 section of COOP_FEATURES_SPEC for the leak positions; the box adds a few meters on every side.
    /// </summary>
    public static readonly StoryGateArea DriveCoreRoom = new("drive core room", new NitroxVector3(-1, -1, -1), new NitroxVector3(1, 1, 1));

    private static readonly TimeSpan clickTimeout = TimeSpan.FromMinutes(2);

    public static StoryGateDefinition For(StoryGateKind kind) =>
        kind switch
        {
            // The click on the radio asks once; the game keeps the message, so a later click asks again.
            StoryGateKind.SUNBEAM_RADIO => new(kind, StoryGateRule.ONLINE_ONLY, TimeSpan.FromSeconds(30)),
            // The welder renews its ready while it keeps welding the last leak.
            StoryGateKind.DRIVE_CORE => new(kind, StoryGateRule.ONE_READY_ALL_PRESENT, TimeSpan.FromSeconds(5), DriveCoreRoom),
            _ => new(kind, StoryGateRule.ALL_READY, clickTimeout)
        };
}
