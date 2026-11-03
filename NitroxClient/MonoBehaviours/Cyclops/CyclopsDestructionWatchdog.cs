using System.Collections;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: a Cyclops that took its killing blow is destroyed 18 s later by a countdown
///     (<c>SubRoot.OnTakeDamage</c>: <c>Invoke("DestroyCyclopsSubRoot", 18f)</c>) that exists only on the game that took the hit.
///     If that player leaves meanwhile, the countdown is gone and the Cyclops would stay at 0 HP for good. Put on a Cyclops known
///     to be at 0 HP; if it still hasn't exploded once the countdown should have ended, the lock holder destroys it the normal way
///     (loot, beacon and the update to everyone, once).
/// </summary>
public class CyclopsDestructionWatchdog : MonoBehaviour
{
    private SubRoot subRoot;

    public static void WatchIfDying(SubRoot subRoot, float health)
    {
        if (!CyclopsDestructionRules.IsDying(health, subRoot.subDestroyed) || subRoot.GetComponent<CyclopsDestructionWatchdog>())
        {
            return;
        }

        subRoot.gameObject.AddComponent<CyclopsDestructionWatchdog>().subRoot = subRoot;
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(CyclopsDestructionRules.WATCHDOG_FIRST_CHECK_SECONDS);

        while (subRoot && subRoot.live && CyclopsDestructionRules.IsDying(subRoot.live.health, subRoot.subDestroyed))
        {
            bool holdsLock = subRoot.TryGetNitroxId(out NitroxId id) && this.Resolve<SimulationOwnership>().HasAnyLockType(id);
            if (CyclopsDestructionRules.ShouldWatchdogDestroy(subRoot.live.health, subRoot.subDestroyed, holdsLock))
            {
                Log.Info($"[{nameof(CyclopsDestructionWatchdog)}] Cyclops {id} is at 0 HP and its countdown never ended (the player who took the hit left): destroying it here");
                subRoot.DestroyCyclopsSubRoot();
                break;
            }

            yield return new WaitForSeconds(CyclopsDestructionRules.WATCHDOG_CHECK_INTERVAL_SECONDS);
        }

        Destroy(this);
    }
}
