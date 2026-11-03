using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S15d: the simulating game's bleeder starts latching onto a player; the other games show it on them.
/// </summary>
public sealed partial class AttachAndSuck_StartPerform_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.StartPerform(default, default));

    public static void Postfix(AttachAndSuck __instance)
    {
        Resolve<Bleeders>().OnAttachStarted(__instance);
    }
}
