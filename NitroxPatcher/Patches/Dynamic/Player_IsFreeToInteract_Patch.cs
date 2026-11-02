using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A downed player can't interact (COOP_FEATURES_SPEC T1): <c>IsFreeToInteract</c> checks <c>IsAlive</c>, and a downed
///     player is alive at 1 health. <c>GUIHand</c> asks it before hand targets, tools and held items.
/// </summary>
public sealed partial class Player_IsFreeToInteract_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Player t) => t.IsFreeToInteract());

    public static void Postfix(ref bool __result)
    {
        if (__result && RescueWindow.IsLocalPlayerDowned)
        {
            __result = false;
        }
    }
}
