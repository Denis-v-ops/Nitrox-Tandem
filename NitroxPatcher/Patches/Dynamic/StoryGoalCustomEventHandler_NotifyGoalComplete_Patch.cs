using System;
using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Applies the gun deactivation for players who didn't use the terminal themselves.
/// </summary>
/// <remarks>
/// <see cref="StoryGoalCustomEventHandler.gunDisabled"/> is only set in Awake and in <see cref="StoryGoalCustomEventHandler.DisableGun"/>,
/// which only runs for the player at the terminal. Other players receive the goal through StoryGoal.Execute, which ends here.
/// Setting the flag before the original method makes its Sunbeam branch take the cancel path, as it does for the terminal user.
/// </remarks>
public sealed partial class StoryGoalCustomEventHandler_NotifyGoalComplete_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalCustomEventHandler t) => t.NotifyGoalComplete(default));

    public static void Prefix(StoryGoalCustomEventHandler __instance, string key)
    {
        if (IsGunDeactivateGoal(key, __instance.gunDeactivate?.key))
        {
            __instance.gunDisabled = true;
        }
    }

    internal static bool IsGunDeactivateGoal(string key, string gunDeactivateKey)
    {
        return !string.IsNullOrEmpty(gunDeactivateKey) && string.Equals(key, gunDeactivateKey, StringComparison.OrdinalIgnoreCase);
    }
}
