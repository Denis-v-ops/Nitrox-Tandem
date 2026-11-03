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
///     The rule of each gate. G1–G3 change their own line here when they hook their moment. The rocket's cockpit moves
///     with the rocket, so <see cref="StoryGateService" /> adds <see cref="RocketCockpit" /> to the rocket launch's rule.
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

    /// <summary>
    ///     Where the rocket's cockpit is relative to the rocket's own position (the launch platform's root): the centre of
    ///     its <c>AtmosphereVolume</c>, from which the game side measures "aboard" (<c>Rockets.RocketLaunch</c>, 5.55 m).
    ///     Read in game through the dev bridge (2026-11-03): rocket at (-60.0, -0.05, 404.85) with no rotation, its
    ///     AtmosphereVolume at (-60.0, 35.1, 404.9), the launch lever at (-60.2, 38.8, 404.8). Straight up, so the
    ///     rocket's yaw doesn't move it (it's still applied).
    /// </summary>
    public static readonly NitroxVector3 RocketCockpitOffset = new(0, 35.2f, 0);

    /// <summary>Half the size of the cockpit box: the game's 5.55 m radius plus room for position lag.</summary>
    public static readonly NitroxVector3 RocketCockpitHalfSize = new(7, 6, 7);

    private static readonly TimeSpan clickTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The box around the cockpit of a rocket standing at <paramref name="rocketPosition" />.</summary>
    public static StoryGateArea RocketCockpit(NitroxVector3 rocketPosition, NitroxQuaternion rocketRotation)
    {
        System.Numerics.Vector3 offset = System.Numerics.Vector3.Transform((System.Numerics.Vector3)RocketCockpitOffset, (System.Numerics.Quaternion)rocketRotation);
        NitroxVector3 centre = rocketPosition + (NitroxVector3)offset;
        return new StoryGateArea("rocket cockpit", centre - RocketCockpitHalfSize, centre + RocketCockpitHalfSize);
    }

    public static StoryGateDefinition For(StoryGateKind kind) =>
        kind switch
        {
            // The click on the radio asks once; the game keeps the message, so a later click asks again.
            StoryGateKind.SUNBEAM_RADIO => new(kind, StoryGateRule.ONLINE_ONLY, TimeSpan.FromSeconds(30)),
            // The welder renews its ready while it keeps welding the last leak.
            StoryGateKind.DRIVE_CORE => new(kind, StoryGateRule.ONE_READY_ALL_PRESENT, TimeSpan.FromSeconds(5), DriveCoreRoom),
            // G3a: a player on the platform renews the ready every second (SeaEmperorPlatformGate); the Emperor comes once both are on it.
            StoryGateKind.SEA_EMPEROR => new(kind, StoryGateRule.ONE_READY_ALL_PRESENT, TimeSpan.FromSeconds(5), AquariumPlatform),
            // G1: each player clicks the terminal (the game only takes the click inside the terminal's area) and gets their
            // own cinematic. Leaving the terminal's area withdraws the click on the game side.
            StoryGateKind.DISABLE_GUN => new(kind, StoryGateRule.ALL_READY, clickTimeout),
            // G2: each player presses launch in the cockpit, so each game launches its own player. The service adds the
            // cockpit's area (RocketCockpit), so a click only counts while its player is still aboard.
            StoryGateKind.ROCKET_LAUNCH => new(kind, StoryGateRule.ALL_READY, clickTimeout),
            // G3c: each player takes a cure ball in their own game (the click proves they are at the babies); a player who
            // swims away from their ball withdraws (CureBallGate).
            StoryGateKind.CURE => new(kind, StoryGateRule.ALL_READY, clickTimeout),
            _ => new(kind, StoryGateRule.ALL_READY, clickTimeout)
        };
}
