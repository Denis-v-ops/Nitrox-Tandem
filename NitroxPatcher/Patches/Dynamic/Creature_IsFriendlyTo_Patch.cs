using System.Reflection;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// FIX_SPEC S14: a creature friendly to "the player" (born in an alien containment, <see cref="WaterParkCreature" />) only
/// spares <see cref="Player.main" /> and vehicles. Now that creatures can bite other players' bodies
/// (MeleeAttack_CanDealDamageTo_Patch), it spares those too. A creature befriended by one player (<see cref="CreatureFriend" />)
/// stays that player's friend only, as in the game.
/// </summary>
public sealed partial class Creature_IsFriendlyTo_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Creature t) => t.IsFriendlyTo(default));

    public static void Postfix(Creature __instance, GameObject gameObject, ref bool __result)
    {
        if (!__result && __instance._friendlyToPlayer && gameObject && gameObject.GetComponent<RemotePlayerIdentifier>())
        {
            __result = true;
        }
    }
}
