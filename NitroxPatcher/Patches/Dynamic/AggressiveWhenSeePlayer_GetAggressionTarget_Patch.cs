using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// FIX_SPEC S14: <see cref="AggressiveWhenSeePlayer" /> (e.g. the crash fish's short-range one) prefers
/// <see cref="Player.main" />, so a creature only ever preferred the player whose game simulates it. It now prefers the
/// nearest valid player, local or remote. When no remote player is valid, the original runs unchanged.
/// </summary>
public sealed partial class AggressiveWhenSeePlayer_GetAggressionTarget_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AggressiveWhenSeePlayer t) => t.GetAggressionTarget());

    public static bool Prefix(AggressiveWhenSeePlayer __instance, ref GameObject __result)
    {
        // Same interval check as the original: right after biting a player, it looks for its other targets
        if (Time.time <= __instance.timeLastPlayerAttack + __instance.playerAttackInterval)
        {
            return true;
        }

        Vector3 position = __instance.transform.position;
        GameObject nearest = null;
        float nearestDistanceSqr = float.MaxValue;
        foreach (RemotePlayer remotePlayer in Resolve<PlayerManager>().GetAll())
        {
            GameObject body = remotePlayer.Body;
            if (!body || !remotePlayer.LiveMixin || !remotePlayer.LiveMixin.IsAlive())
            {
                continue;
            }
            float distanceSqr = (body.transform.position - position).sqrMagnitude;
            // IsTargetValid also checks RemotePlayer.CanBeAttacked (AggressiveWhenSeeTarget_IsTargetValid_Patch)
            if (distanceSqr < nearestDistanceSqr && __instance.IsTargetValid(body))
            {
                nearest = body;
                nearestDistanceSqr = distanceSqr;
            }
        }
        if (!nearest)
        {
            return true;
        }

        GameObject localPlayer = Player.main ? Player.main.gameObject : null;
        if (localPlayer && (localPlayer.transform.position - position).sqrMagnitude <= nearestDistanceSqr && __instance.IsTargetValid(localPlayer))
        {
            nearest = localPlayer;
        }
        __result = nearest;
        return false;
    }
}
