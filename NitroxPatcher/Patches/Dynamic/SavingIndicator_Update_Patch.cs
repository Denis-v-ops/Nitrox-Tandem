using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     MULTINAUGHTICA_SPEC M7: the game's "Saving game" indicator also shows while the server saves.
///     <c>SavingIndicator.Update</c> sets <c>canvasGroup.alpha</c> to 1 only while the game saves itself
///     (<c>SaveLoadManager.main.isSaving</c>), which a Nitrox game never does.
/// </summary>
public sealed partial class SavingIndicator_Update_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SavingIndicator t) => t.Update());

    public static void Postfix(SavingIndicator __instance)
    {
        if (Resolve<ServerSaveIndicator>().OnIndicatorUpdate(Time.frameCount, Time.unscaledTime))
        {
            __instance.canvasGroup.alpha = 1f;
        }
    }
}
