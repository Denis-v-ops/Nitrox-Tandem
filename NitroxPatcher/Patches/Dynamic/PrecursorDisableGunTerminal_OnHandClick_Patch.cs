using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC G1: a cured player's click on the gun terminal waits until every player clicked their terminal,
///     then each game plays its own cinematic at the same moment. Without it, the first player disables the gun alone and
///     the terminal never opens again for the partner (<c>OnTerminalAreaEnter</c> checks <c>gunDisabled</c>). The
///     uncured player's "access denied" click isn't held.
/// </summary>
public sealed partial class PrecursorDisableGunTerminal_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorDisableGunTerminal t) => t.OnHandClick(default));

    public static bool Prefix(PrecursorDisableGunTerminal __instance, GUIHand hand)
    {
        // The game's own checks: anything else does nothing, or is the access denied path.
        if (!__instance.playerCured || !__instance.opened || __instance.usingPlayer || PlayerCinematicController.cinematicModeCount > 0)
        {
            return true;
        }
        StoryGates storyGates = Resolve<StoryGates>();
        if (storyGates.IsPassing(StoryGateKind.DISABLE_GUN))
        {
            return true;
        }
        storyGates.Hold(StoryGateKind.DISABLE_GUN, () =>
        {
            if (__instance)
            {
                __instance.OnHandClick(hand);
            }
        });
        return false;
    }
}
