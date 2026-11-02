using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Everyone in the sub can use the helm screen (Cyclops crew, COOP_FEATURES_SPEC T4), but only the pilot sounds the
///     horn.
/// </summary>
public sealed partial class CyclopsHornButton_OnPress_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsHornButton t) => t.OnPress());

    public static bool Prefix()
    {
        return Player.main.isPiloting;
    }
}
