using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC B6: a repulsion shot pushes an object in both games. The game only sets the object's velocity in
///     the shooter's game (<c>RepulsionCannon.ShootObject</c>), so an object that the partner's game simulates (or that
///     nobody simulates, like a loose item) snapped back or stayed put for the partner. As
///     <see cref="PropulsionCannon_GrabObject_Patch" /> does for a grab: take the exclusive lock first (a normal lock can't
///     be taken from the partner), then push it, send its position while it flies and keep a normal lock. If the partner
///     holds it exclusively (grabbed with their cannon, piloting it), the shot doesn't push it in either game.
/// </summary>
public sealed partial class RepulsionCannon_ShootObject_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((RepulsionCannon t) => t.ShootObject(default(Rigidbody), default(Vector3)));

    private static bool skipPrefix;

    public static bool Prefix(RepulsionCannon __instance, Rigidbody rb, Vector3 velocity)
    {
        if (skipPrefix || !rb)
        {
            return true;
        }

        GameObject target = rb.gameObject;
        // The partner's body moves with their own game
        if (target.GetComponentInParent<RemotePlayerIdentifier>())
        {
            return false;
        }

        if (!target.TryGetNitroxId(out NitroxId id) || Resolve<SimulationOwnership>().HasExclusiveLock(id))
        {
            return true;
        }

        RepulsionShot context = new(__instance, rb, velocity);
        Resolve<SimulationOwnership>().RequestSimulationLock(new LockRequest<RepulsionShot>(id, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, context));
        return false;
    }

    private static void ReceivedSimulationLockResponse(NitroxId id, bool lockAcquired, RepulsionShot context)
    {
        if (!lockAcquired || !context.Cannon || !context.Body)
        {
            return;
        }

        SimulationOwnership simulationOwnership = Resolve<SimulationOwnership>();
        if (!simulationOwnership.TreatVehicleEntity(id, true, SimulationLockType.EXCLUSIVE))
        {
            EntityPositionBroadcaster.WatchEntity(id);
        }

        skipPrefix = true;
        try
        {
            context.Cannon.ShootObject(context.Body, context.Velocity);
        }
        finally
        {
            skipPrefix = false;
        }

        simulationOwnership.RequestSimulationLock(id, SimulationLockType.TRANSIENT);
    }

    private sealed class RepulsionShot : LockRequestContext
    {
        public RepulsionCannon Cannon { get; }
        public Rigidbody Body { get; }
        public Vector3 Velocity { get; }

        public RepulsionShot(RepulsionCannon cannon, Rigidbody body, Vector3 velocity)
        {
            Cannon = cannon;
            Body = body;
            Velocity = velocity;
        }
    }
}
