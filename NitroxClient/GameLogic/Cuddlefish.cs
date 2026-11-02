using System;
using System.Collections;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Gui.HUD;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC P1: the cuddlefish follows whoever played with it last. The game makes the local player its
///     friend on every game (<c>CuteFish.Start</c>), and only the game with its simulation lock runs its AI, so moving the
///     lock is enough: playing with it or giving it a command takes the lock as exclusive (as sitting on a bench does) and
///     drops back to a normal lock once the game is done with it. The other player can take it the same way.
/// </summary>
public class Cuddlefish
{
    private readonly SimulationOwnership simulationOwnership;
    private readonly Entities entities;

    public Cuddlefish(SimulationOwnership simulationOwnership, Entities entities)
    {
        this.simulationOwnership = simulationOwnership;
        this.entities = entities;
    }

    /// <summary>
    ///     True when this game simulates the cuddlefish (or it has no id yet, as in single player).
    /// </summary>
    public bool IsSimulatedHere(CuteFish cuteFish)
    {
        return !cuteFish.TryGetNitroxId(out NitroxId id) || simulationOwnership.HasAnyLockType(id);
    }

    /// <summary>
    ///     True when the interaction can go on now: this game already has the exclusive lock (or the cuddlefish has no id).
    ///     Otherwise asks the server for it and returns false; <paramref name="onAcquired" /> runs once it's granted.
    /// </summary>
    public bool HasExclusiveLockOrRequest(CuteFishHandTarget handTarget, Action onAcquired)
    {
        if (!handTarget.cuteFish.TryGetIdOrWarn(out NitroxId id) || simulationOwnership.HasExclusiveLock(id))
        {
            return true;
        }

        LockRequest<Interaction> lockRequest = new(id, SimulationLockType.EXCLUSIVE, ReceivedLockResponse, new Interaction(handTarget, onAcquired));
        simulationOwnership.RequestSimulationLock(lockRequest);
        return false;
    }

    public void BroadcastState(CuteFish cuteFish)
    {
        if (cuteFish.TryGetIdOrWarn(out NitroxId id))
        {
            entities.EntityMetadataChanged(cuteFish, id);
        }
    }

    private void ReceivedLockResponse(NitroxId id, bool lockAcquired, Interaction interaction)
    {
        CuteFishHandTarget handTarget = interaction.HandTarget;
        if (!lockAcquired)
        {
            // The other player is playing with it right now
            if (handTarget)
            {
                handTarget.gameObject.EnsureComponent<DenyOwnershipHand>();
            }
            return;
        }

        // A lock granted on request doesn't start the position broadcast (SimulationOwnershipResponseProcessor); the
        // cuddlefish swims, so the other games need its moves from now on.
        EntityPositionBroadcaster.WatchEntity(id);
        if (handTarget)
        {
            interaction.OnAcquired();
        }
        CoroutineHost.StartCoroutine(DropToNormalLockWhenDone(id, handTarget));
    }

    /// <summary>
    ///     Play scene, command or farewell: once the hand target is idle again, keep the lock as a normal one so this game
    ///     goes on simulating it (it follows this player) until the other player plays with it.
    /// </summary>
    private IEnumerator DropToNormalLockWhenDone(NitroxId id, CuteFishHandTarget handTarget)
    {
        yield return null;
        yield return new WaitUntil(() => !handTarget || handTarget.state == CuteFishHandTarget.State.None);
        if (simulationOwnership.HasExclusiveLock(id))
        {
            simulationOwnership.RequestSimulationLock(id, SimulationLockType.TRANSIENT);
        }
    }

    private sealed class Interaction : LockRequestContext
    {
        public CuteFishHandTarget HandTarget { get; }
        public Action OnAcquired { get; }

        public Interaction(CuteFishHandTarget handTarget, Action onAcquired)
        {
            HandTarget = handTarget;
            OnAcquired = onAcquired;
        }
    }
}
