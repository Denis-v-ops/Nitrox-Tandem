using System.Reflection;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sends the charge of base power sources from the game holding the base's lock (COOP_FEATURES_SPEC B3, #1811); see
/// <see cref="BasePowerSync" /> for when.
/// </summary>
/// <remarks>
/// <c>PowerSource.Start</c> (game 83031) runs <c>UpdateConnectionCallback</c> once a second with <c>InvokeRepeating</c>
/// for every active power source, which makes it a cheap tick that also sees changes made by setting the field.
/// </remarks>
public sealed partial class PowerSource_UpdateConnectionCallback_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PowerSource t) => t.UpdateConnectionCallback());

    public static void Postfix(PowerSource __instance)
    {
        Resolve<BasePowerSync>().Tick(__instance);
    }
}
