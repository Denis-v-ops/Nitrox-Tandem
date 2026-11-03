using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Story;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC G3a: the Sea Emperor's platform waits for both players. Without this, the first player on the
///     platform starts the Emperor's arrival alone, and the partner hears the dialog wherever they are (its goals are
///     relayed). Held, <see cref="SeaEmperorPlatformGate" /> renews the gate while the local player stands on the platform;
///     on the gate's start each game runs the platform itself.
/// </summary>
public sealed partial class PrecursorAquariumPlatformTrigger_OnTriggerEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorAquariumPlatformTrigger t) => t.OnTriggerEnter(default));

    public static bool Prefix(PrecursorAquariumPlatformTrigger __instance, Collider other)
    {
        // Only what the game itself would start (game:PrecursorAquariumPlatformTrigger.cs 46-48): the local player entering
        // a platform that hasn't run yet. Once the Emperor has spoken (e.g. the gate was released for one player), the
        // platform plays as in single player.
        if (__instance.triggered || other.isTrigger || !Player.main || PrecursorAquariumPlatformTrigger.FindPlayer(other) != Player.main ||
            (StoryGoalManager.main && StoryGoalManager.main.IsGoalComplete(SeaEmperorPlatformGate.FIRST_DIALOG_GOAL)))
        {
            return true;
        }
        SeaEmperorPlatformGate.Watch(__instance, Resolve<StoryGates>());
        return false;
    }
}
