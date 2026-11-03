using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): the hull of a Seamoth a partner pilots offers "hold on" instead of
///     "enter" (which the partner's lock would refuse anyway).
/// </summary>
public sealed partial class Vehicle_OnHandHover_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.OnHandHover(default(GUIHand)));

    public static bool Prefix(Vehicle __instance)
    {
        return !Resolve<SeamothHitch>().TryHover(__instance);
    }
}
