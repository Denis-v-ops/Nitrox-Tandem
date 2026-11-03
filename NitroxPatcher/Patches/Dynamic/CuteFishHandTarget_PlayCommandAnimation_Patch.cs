using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC P1: a follow/stay command reaches the server (kept in the cuddlefish's metadata) and the other games.
/// </summary>
public sealed partial class CuteFishHandTarget_PlayCommandAnimation_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CuteFishHandTarget t) => t.PlayCommandAnimation());

    public static void Postfix(CuteFishHandTarget __instance, bool __result)
    {
        if (__result)
        {
            Resolve<Cuddlefish>().BroadcastState(__instance.cuteFish);
        }
    }
}
