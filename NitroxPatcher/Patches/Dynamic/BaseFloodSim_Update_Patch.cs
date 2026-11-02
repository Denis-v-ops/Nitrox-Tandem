using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Applies flood levels received from the server once the base's flood grid exists, and lets the base's lock holder
///     send its levels (see <see cref="BaseFlooding" />).
/// </summary>
public sealed partial class BaseFloodSim_Update_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((BaseFloodSim t) => t.Update());

    public static void Postfix(BaseFloodSim __instance)
    {
        Resolve<BaseFlooding>().AfterUpdate(__instance);
    }
}
