using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LaunchRocket_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((LaunchRocket t) => t.OnHandClick(default));

    /// <summary>
    ///     COOP_FEATURES_SPEC G2: the launch waits until every player pressed launch in the cockpit, then each game launches
    ///     its own player at once. Only a click that would launch is held (the same checks as the game's, before
    ///     <c>SetLaunchStarted</c>); the gun warning and a rocket that isn't ready behave as before.
    /// </summary>
    public static bool Prefix(LaunchRocket __instance, GUIHand hand)
    {
        if (!__instance.IsRocketReady() || LaunchRocket.launchStarted ||
            (!StoryGoalCustomEventHandler.main.gunDisabled && !__instance.forcedRocketReady))
        {
            return true;
        }
        StoryGates storyGates = Resolve<StoryGates>();
        if (storyGates.IsPassing(StoryGateKind.ROCKET_LAUNCH))
        {
            // A player who left the cockpit since the click stays behind rather than starting a launch without them.
            Rocket rocket = __instance.GetComponentInParent<Rocket>();
            return !rocket || Rockets.IsAboard(rocket.gameObject);
        }
        storyGates.Hold(StoryGateKind.ROCKET_LAUNCH, () =>
        {
            if (__instance)
            {
                __instance.OnHandClick(hand);
            }
        });
        return false;
    }

    /*
     * 
     * LaunchRocket.SetLaunchStarted();
     * LaunchRocket_OnHandClick_Patch.BroadcastRocketLaunch(this);   <--- [INSERTED LINE]
     * [Removed all lines after]
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchEndForward([
                                                new CodeMatch(OpCodes.Call, Reflect.Method(() => LaunchRocket.SetLaunchStarted()))
                                            ])
                                            .Advance(1) // Skip the SetLaunchStarted because we don't want to manage all the jump labels that forward to it
                                            // Also this instruction doesn't block Rockets.RocketLaunch(GameObject) since that function doesn't check for LaunchRocket.launchStarted
                                            .RemoveInstructions(10)
                                            .InsertAndAdvance([
                                                new CodeInstruction(OpCodes.Ldarg_0),
                                                new CodeInstruction(OpCodes.Call, Reflect.Method(() => BroadcastRocketLaunch(default)))
                                            ]).InstructionEnumeration();
    }

    private static void BroadcastRocketLaunch(LaunchRocket launchRocket)
    {
        Rocket rocket = launchRocket.RequireComponentInParent<Rocket>();
        Rockets rockets = Resolve<Rockets>();
        rockets.RequestRocketLaunch(rocket);
        rockets.RocketLaunch(rocket.gameObject);
    }
}
