using System.Collections.Generic;
using Nitrox.Model.DataStructures;

namespace NitroxClient.MonoBehaviours.CinematicController;

/// <summary>
///     The exclusive locks the cinematic patches take before the local player uses a hatch, ladder, bed side or terminal
///     (upstream #2859), so two players can't play one cinematic at once (COOP_FEATURES_SPEC R1b).
/// </summary>
/// <remarks>
///     A cinematic's lock is keyed on the entity above its controller (a bed side has its own, <see cref="BedLockId" />).
///     Only the locks these patches took themselves are given back when the cinematic ends: the same id can be a lock that
///     another system holds and gives back itself, e.g. the bench seat (<c>Bench_OnHandClick_Patch</c>) or the
///     cuddlefish (P1).
/// </remarks>
public static class CinematicLocks
{
    private static readonly HashSet<NitroxId> takenByCinematics = [];

    /// <summary>
    ///     Whether a cinematic under this entity takes a lock. Not for a Cyclops, a base or a vehicle: their id is the lock
    ///     that steering and simulating them uses (a ladder in the Cyclops would take the helm from the pilot, a base hatch
    ///     would block every other hatch of the base and move its power, B3), and their own patches lock what needs it
    ///     (<c>PilotingChair_OnHandClick_Patch</c>, <c>DockedVehicleHandTarget_OnHandClick_Patch</c>). Not for a docking
    ///     bay (it has an id of its own): its docking cinematic starts from <c>VehicleDockingBay.LateUpdate</c> and must
    ///     start at once, and the docked vehicle's lock guards undocking (seen windowless: S6 docked 1 of 5 times with the
    ///     lock). Not for a creature either: its lock is its simulation, a creature's attack cinematic must start at once,
    ///     and the cuddlefish locks itself (P1).
    /// </summary>
    public static bool UsesLock(NitroxEntity entity)
    {
        return !entity.GetComponent<SubRoot>() && !entity.GetComponent<Vehicle>() && !entity.GetComponent<VehicleDockingBay>() && !entity.GetComponent<Creature>();
    }

    /// <summary>
    ///     The patches got this lock for a cinematic: <see cref="Release" /> gives it back when the cinematic ends.
    /// </summary>
    public static void Taken(NitroxId lockId)
    {
        takenByCinematics.Add(lockId);
    }

    /// <returns>True when the lock was taken for a cinematic and must now be downgraded.</returns>
    public static bool Release(NitroxId lockId)
    {
        return takenByCinematics.Remove(lockId);
    }

    /// <summary>
    ///     A bed's lie-down cinematic ends when the player lies down; the side stays theirs until they get up
    ///     (<c>Bed_ExitInUseMode_Patch</c>).
    /// </summary>
    public static bool IsBedLieDown(PlayerCinematicController controller)
    {
        return controller.TryGetComponentInParent(out Bed bed, true) && (controller == bed.leftLieDownCinematicController || controller == bed.rightLieDownCinematicController);
    }
}
