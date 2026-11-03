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

/// <summary>A box in world coordinates, checked against the positions players last reported.</summary>
/// <param name="YawDegrees">
///     The box turned by this angle around the vertical axis through its center, like a Unity transform with the euler angles
///     (0, yaw, 0): <paramref name="Min" /> and <paramref name="Max" /> are the box before turning. 0 = axis-aligned.
/// </param>
internal sealed record StoryGateArea(string Name, NitroxVector3 Min, NitroxVector3 Max, float YawDegrees = 0)
{
    /// <summary>A box like a Unity <c>BoxCollider</c>: world center, size and the transform's yaw.</summary>
    public static StoryGateArea Box(string name, NitroxVector3 center, NitroxVector3 size, float yawDegrees) =>
        new(name, center - size / 2f, center + size / 2f, yawDegrees);

    public bool Contains(NitroxVector3 position)
    {
        if (YawDegrees != 0)
        {
            // Turn the position back by the yaw around the center (Unity: world = rotation * local, y up, left-handed).
            NitroxVector3 center = (Min + Max) / 2f;
            double yaw = YawDegrees * Math.PI / 180;
            float dx = position.X - center.X;
            float dz = position.Z - center.Z;
            position = new NitroxVector3(center.X + (float)(dx * Math.Cos(yaw) - dz * Math.Sin(yaw)),
                                         position.Y,
                                         center.Z + (float)(dx * Math.Sin(yaw) + dz * Math.Cos(yaw)));
        }
        return position.X >= Min.X && position.X <= Max.X &&
               position.Y >= Min.Y && position.Y <= Max.Y &&
               position.Z >= Min.Z && position.Z <= Max.Z;
    }
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
    ///     The Aurora's drive core room: the 11 radiation leaks lie within x 849.9..881.5, y -16.0..2.7, z -7.7..18.9
    ///     (read in game through the dev bridge, 2026-11-02); the box adds 6 m on every side. The game has no volume or
    ///     biome of its own for the room (it reports "crashedShip" all over the wreck), and the walls weren't measured.
    /// </summary>
    public static readonly StoryGateArea DriveCoreRoom = new("drive core room", new NitroxVector3(844, -22, -14), new NitroxVector3(888, 9, 25));

    /// <summary>
    ///     The Sea Emperor's platform in the aquarium of the Primary Containment Facility: the game's own trigger volume
    ///     (<c>PrecursorAquariumPlatformTrigger</c>'s <c>BoxCollider</c>, size 50 x 15 x 70, turned 151 degrees), read in
    ///     game through the dev bridge 2026-11-03 (game 83031). Points were checked with <c>BoxCollider.ClosestPoint</c>.
    /// </summary>
    public static readonly StoryGateArea AquariumPlatform = StoryGateArea.Box("aquarium platform", new NitroxVector3(315.89f, -1470.5f, -438.566f), new NitroxVector3(50, 15, 70), 151);

    private static readonly TimeSpan clickTimeout = TimeSpan.FromMinutes(2);

    public static StoryGateDefinition For(StoryGateKind kind) =>
        kind switch
        {
            // The click on the radio asks once; the game keeps the message, so a later click asks again.
            StoryGateKind.SUNBEAM_RADIO => new(kind, StoryGateRule.ONLINE_ONLY, TimeSpan.FromSeconds(30)),
            // The welder renews its ready while it keeps welding the last leak.
            StoryGateKind.DRIVE_CORE => new(kind, StoryGateRule.ONE_READY_ALL_PRESENT, TimeSpan.FromSeconds(5), DriveCoreRoom),
            // G3a: a player on the platform renews the ready every second (SeaEmperorPlatformGate); the Emperor comes once both are on it.
            StoryGateKind.SEA_EMPEROR => new(kind, StoryGateRule.ONE_READY_ALL_PRESENT, TimeSpan.FromSeconds(5), AquariumPlatform),
            // G3c: each player takes a cure ball in their own game (the click proves they are at the babies); a player who
            // swims away from their ball withdraws (CureBallGate).
            StoryGateKind.CURE => new(kind, StoryGateRule.ALL_READY, clickTimeout),
            _ => new(kind, StoryGateRule.ALL_READY, clickTimeout)
        };
}
