using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M4, FIX_SPEC S15d: a Bleeder's attack (<see cref="AttachAndSuck" />) is a creature action, and
///     actions only run in the game that simulates the creature. The contact that starts it,
///     <c>AttachAndSuck.OnCollisionEnter</c>, is a physics callback that runs on every game's copy (a copy that another
///     game simulates still swims and collides). Before, the copy in the victim's game wrote the contact into its state
///     (the player's <c>BleederAttachTarget</c> became occupied, which spared that player from every Bleeder in their game)
///     and nothing else happened. Now the player whose copy accepted the contact asks for the Bleeder's exclusive lock: the
///     whole attack then runs vanilla in their game, against their own player. The usual lock rules arbitrate (a second
///     player can't take an attached Bleeder, the hand-over between players skips exclusive locks). When the Bleeder lets
///     go, the lock goes back to the normal hand-over. No new packet.
/// </summary>
/// <remarks>Ported from MultiNaughtica (K18), adapted.</remarks>
public static class BleederClaims
{
    /// <summary>
    ///     Seconds between the Bleeder letting go and the lock going back: long enough for the Propulsion Cannon, which grabs
    ///     the Bleeder right after it pulled it off, to be seen.
    /// </summary>
    private const float RELEASE_DELAY = 0.5f;

    /// <summary>
    ///     Seconds between two looks at whether the Propulsion Cannon still holds a claimed Bleeder.
    /// </summary>
    private const float CANNON_RECHECK_INTERVAL = 1f;

    /// <summary>
    ///     Seconds after the grant by which the attack must have started, or the contact is undone (see
    ///     <see cref="BleederClaimRules.ShouldAbandonClaim" />).
    /// </summary>
    private const float ATTACK_START_TIMEOUT = 5f;

    private static readonly Dictionary<NitroxId, float> requestTimeById = [];

    /// <summary>
    ///     This game's copy of the Bleeder just accepted a contact (the game found the target free and attackable).
    /// </summary>
    public static void Claim(AttachAndSuck attack, NitroxId id, SimulationOwnership simulationOwnership)
    {
        bool holdsExclusiveLock = simulationOwnership.HasExclusiveLock(id);
        // Only the local player's attack can run here. Another target is the simulating game's business.
        if (!Player.main || attack.targetLiveMixin != Player.main.liveMixin)
        {
            if (!simulationOwnership.HasAnyLockType(id))
            {
                attack.SetDetached();
            }
            return;
        }

        float? secondsSinceRequest = requestTimeById.TryGetValue(id, out float requestTime) ? Time.time - requestTime : null;
        if (!BleederClaimRules.ShouldRequestClaim(holdsExclusiveLock, secondsSinceRequest))
        {
            if (!holdsExclusiveLock)
            {
                // The game wrote the contact into the copy and the player, and no answer will come for it
                attack.SetDetached();
            }
            return;
        }

        requestTimeById[id] = Time.time;
        Log.Info($"[Bleeder] {attack.name} touches the local player, asking for its simulation lock {id}");
        simulationOwnership.RequestSimulationLock(new LockRequest<BleederClaim>(id, SimulationLockType.EXCLUSIVE,
                                                                               (lockId, lockAcquired, claim) => ReceivedSimulationLockResponse(lockId, lockAcquired, claim, simulationOwnership),
                                                                               new BleederClaim(attack)));
    }

    private static void ReceivedSimulationLockResponse(NitroxId id, bool lockAcquired, BleederClaim claim, SimulationOwnership simulationOwnership)
    {
        requestTimeById.Remove(id);
        AttachAndSuck attack = claim.Attack;
        bool stillHasTarget = attack && attack.currentTarget;
        BleederClaimRules.ClaimOutcome outcome = BleederClaimRules.JudgeClaimResponse(lockAcquired, stillHasTarget);
        Log.Info($"[Bleeder] simulation lock for {id}: {(lockAcquired ? "acquired" : "denied")}, {outcome}");

        switch (outcome)
        {
            case BleederClaimRules.ClaimOutcome.Detach:
                // Another player holds the Bleeder exclusively (it is on them): free the local player for other Bleeders
                if (attack)
                {
                    attack.SetDetached();
                }
                break;
            case BleederClaimRules.ClaimOutcome.ReleaseLock:
                // The lock is ours until the server answers, and a creature of ours streams its position
                EntityPositionBroadcaster.WatchEntity(id);
                simulationOwnership.RequestSimulationLock(id, SimulationLockType.TRANSIENT);
                break;
            case BleederClaimRules.ClaimOutcome.KeepLock:
                // Nothing else starts the position stream of a creature another player simulated
                EntityPositionBroadcaster.WatchEntity(id);
                CoroutineHost.StartCoroutine(WatchAttackStart(attack, id, simulationOwnership));
                break;
        }
    }

    private static IEnumerator WatchAttackStart(AttachAndSuck attack, NitroxId id, SimulationOwnership simulationOwnership)
    {
        yield return new WaitForSeconds(ATTACK_START_TIMEOUT);
        if (attack && BleederClaimRules.ShouldAbandonClaim(simulationOwnership.HasExclusiveLock(id), attack.currentTarget, attack.active))
        {
            Log.Info($"[Bleeder] the attack of {id} didn't start within {ATTACK_START_TIMEOUT:F0} s of the grant, undoing the contact");
            // Its patch gives the lock back
            attack.SetDetached();
        }
    }

    /// <summary>
    ///     The Bleeder let go of its target (<c>AttachAndSuck.SetDetached</c>, the attack's only exit): the exclusive lock goes
    ///     back to the hand-over of creatures between players.
    /// </summary>
    public static void OnDetached(AttachAndSuck attack, NitroxId id, SimulationOwnership simulationOwnership)
    {
        // A claim waiting for its answer owns this id's lock requests: the answer to a plain request would be taken for it
        if (requestTimeById.ContainsKey(id) || !simulationOwnership.HasExclusiveLock(id))
        {
            return;
        }

        CoroutineHost.StartCoroutine(ReleaseLater(attack, id, simulationOwnership));
    }

    private static IEnumerator ReleaseLater(AttachAndSuck attack, NitroxId id, SimulationOwnership simulationOwnership)
    {
        yield return new WaitForSeconds(RELEASE_DELAY);

        // Gone with its cell (the server takes the lock of a removed entity), the lock went elsewhere, or a new contact started a claim
        while (attack && !requestTimeById.ContainsKey(id) && simulationOwnership.HasExclusiveLock(id))
        {
            // A new contact accepted meanwhile keeps the lock
            if (attack.currentTarget)
            {
                yield break;
            }

            // The Propulsion Cannon grabs the Bleeder right after pulling it off. It gives the lock back when it lets go
            // with ReleaseGrabbedObject, but a shot never calls that (PropulsionCannon.OnShoot clears the grab itself).
            if (IsHeldByPropulsionCannon(attack.gameObject))
            {
                yield return new WaitForSeconds(CANNON_RECHECK_INTERVAL);
                continue;
            }

            LiveMixin liveMixin = attack.GetComponent<LiveMixin>();
            bool alive = !liveMixin || liveMixin.IsAlive();
            if (BleederClaimRules.ShouldReleaseLockAfterDetach(true, attack.isActiveAndEnabled, alive))
            {
                simulationOwnership.RequestSimulationLock(id, SimulationLockType.TRANSIENT);
            }
            yield break;
        }
    }

    private static bool IsHeldByPropulsionCannon(GameObject creatureObject)
    {
        return Inventory.main && Inventory.main.GetHeldTool() is PropulsionCannonWeapon weapon && weapon.propulsionCannon && weapon.propulsionCannon.grabbedObject == creatureObject;
    }

    /// <summary>
    ///     The server took an entity's simulation from this game (<see cref="SimulationOwnership" />). A Bleeder in the middle
    ///     of its attack is detached here, because the game does it in <c>StopPerform</c>, which doesn't run for a creature
    ///     this game doesn't simulate. Does nothing for any other entity.
    /// </summary>
    public static void OnLockLost(NitroxId id)
    {
        requestTimeById.Remove(id);
        if (NitroxEntity.TryGetComponentFrom(id, out AttachAndSuck attack) && BleederClaimRules.NeedsDetachAfterLockLoss(attack.active, attack.currentTarget))
        {
            Log.Info($"[Bleeder] lost the simulation of {id} during its attack, ending the attack here");
            attack.SetDetached();
            attack.active = false;
        }
    }
}
