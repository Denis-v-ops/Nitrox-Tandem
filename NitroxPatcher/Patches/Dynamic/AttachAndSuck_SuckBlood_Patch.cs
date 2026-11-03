using System.Reflection;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S15d: a bleeder sucking a partner's body names itself as the dealer, so LiveMixin_TakeDamage_Patch sends
///     the damage to the partner (S14's <c>CreatureAttackedPlayer</c>); the game's suck passes no dealer. The rest is
///     <c>AttachAndSuck.SuckBlood</c> (game 83031). The body can't die, so the game's "let go when the target died" is
///     left to the body's state (<c>Bleeders.AfterUpdate</c>).
/// </summary>
public sealed partial class AttachAndSuck_SuckBlood_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.SuckBlood());

    public static bool Prefix(AttachAndSuck __instance)
    {
        LiveMixin target = __instance.targetLiveMixin;
        if (!target || !target.GetComponent<RemotePlayerIdentifier>())
        {
            return true;
        }

        float time = Time.time;
        if (__instance.timeLastSuck + 1f < time)
        {
            __instance.timeLastSuck = time;
            __instance.GetComponent<LiveMixin>().AddHealth(1f);
            target.TakeDamage(__instance.leechDamage, __instance.transform.position, DamageType.Normal, __instance.gameObject);
            __instance.gameObject.SendMessage("OnSuck", SendMessageOptions.DontRequireReceiver);
        }
        return false;
    }
}
