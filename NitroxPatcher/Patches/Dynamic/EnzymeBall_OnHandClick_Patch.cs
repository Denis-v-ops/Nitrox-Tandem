using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC G3c: the cure starts once both players have taken a cure ball, then each game plays its own
///     player's cure cinematic. <c>EnzymeBall.OnHandClick</c> (reached through the ball's <c>GenericHandTarget</c>) does
///     nothing once the cure goal is complete (<c>playerCured</c>), so only a click that would cure is held.
/// </summary>
public sealed partial class EnzymeBall_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((EnzymeBall t) => t.OnHandClick());

    public static bool Prefix(EnzymeBall __instance)
    {
        if (__instance.playerCured)
        {
            return true;
        }
        StoryGates storyGates = Resolve<StoryGates>();
        if (storyGates.IsPassing(StoryGateKind.CURE))
        {
            CureBallGate.OnCureStarts(__instance);
            return true;
        }
        CureBallGate.Hold(__instance, storyGates);
        return false;
    }
}
