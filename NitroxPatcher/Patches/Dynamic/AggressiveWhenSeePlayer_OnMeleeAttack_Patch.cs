using System.Reflection;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// FIX_SPEC S14: after biting a player, <see cref="AggressiveWhenSeePlayer" /> stops preferring players for
/// <see cref="AggressiveWhenSeePlayer.playerAttackInterval" />. The original only counts the local player; other players'
/// bodies count too, now that it can bite them (MeleeAttack_CanDealDamageTo_Patch).
/// </summary>
public sealed partial class AggressiveWhenSeePlayer_OnMeleeAttack_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AggressiveWhenSeePlayer t) => t.OnMeleeAttack(default));

    public static void Postfix(AggressiveWhenSeePlayer __instance, GameObject target)
    {
        if (target && target.GetComponent<RemotePlayerIdentifier>())
        {
            __instance.timeLastPlayerAttack = Time.time;
        }
    }
}
