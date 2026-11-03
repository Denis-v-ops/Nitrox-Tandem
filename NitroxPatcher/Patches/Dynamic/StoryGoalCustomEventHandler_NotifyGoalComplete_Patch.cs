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

    /// <summary>
    ///     FIX_SPEC S3 step 2 (MULTINAUGHTICA_SPEC M2): a terminal that stood open in this game when the partner disabled the
    ///     gun closes, as the user's own does (<c>PrecursorDisableGunTerminal.OnPlayerCinematicModeEnd</c>). Once
    ///     <c>gunDisabled</c> is set it never opens again (<c>OnTerminalAreaEnter</c>), but an open one still took clicks
    ///     (<c>OnHandClick</c> doesn't check the flag) and played its cinematic again.
    /// </summary>
    public static void Postfix(StoryGoalCustomEventHandler __instance, string key)
    {
        if (!IsGunDeactivateGoal(key, __instance.gunDeactivate?.key))
        {
            return;
        }
        foreach (PrecursorDisableGunTerminal terminal in UnityEngine.Object.FindObjectsOfType<PrecursorDisableGunTerminal>())
        {
            // A terminal in use closes at the end of its own cinematic
            if (terminal.opened && !terminal.usingPlayer)
            {
                terminal.SetOpen(false);
            }
        }
    }

    internal static bool IsGunDeactivateGoal(string key, string gunDeactivateKey)
    {
        return !string.IsNullOrEmpty(gunDeactivateKey) && string.Equals(key, gunDeactivateKey, StringComparison.OrdinalIgnoreCase);
    }
}
