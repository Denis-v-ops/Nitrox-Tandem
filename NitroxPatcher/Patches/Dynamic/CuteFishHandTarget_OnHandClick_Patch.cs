using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC P1: playing with the cuddlefish first takes its simulation lock as exclusive, so it follows the
///     player who played with it last (see <see cref="Cuddlefish" />). The click runs again once the lock is granted.
/// </summary>
public sealed partial class CuteFishHandTarget_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CuteFishHandTarget t) => t.OnHandClick(default(GUIHand)));
    private static bool skipPrefix;

    public static bool Prefix(CuteFishHandTarget __instance, GUIHand hand)
    {
        // Only a click the game would act on asks for the lock (the same checks as OnHandClick)
        if (skipPrefix || !__instance.AllowedToInteract() || GameOptions.GetVrAnimationMode() || !__instance.isValidHandTarget)
        {
            return true;
        }

        return Resolve<Cuddlefish>().HasExclusiveLockOrRequest(__instance, () =>
        {
            skipPrefix = true;
            try
            {
                __instance.OnHandClick(hand);
            }
            finally
            {
                skipPrefix = false;
            }
        });
    }
}
