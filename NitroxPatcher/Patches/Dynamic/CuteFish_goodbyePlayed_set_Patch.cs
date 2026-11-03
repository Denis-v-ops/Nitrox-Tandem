using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC P1: the farewell (and the "hellofish" console command that undoes it) reaches the server and the
///     other games. The metadata processor sets the field directly, so applying a received state doesn't come back here.
/// </summary>
public sealed partial class CuteFish_goodbyePlayed_set_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Property((CuteFish t) => t.goodbyePlayed).SetMethod;

    public static void Postfix(CuteFish __instance)
    {
        Resolve<Cuddlefish>().BroadcastState(__instance);
    }
}
