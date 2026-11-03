using System.Reflection;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     The builder in a partner's hand beams at the ghost they build (COOP_FEATURES_SPEC R2c). <c>BuilderTool.LateUpdate</c>
///     runs on that copy too (it isn't behind <c>isDrawn</c>) and draws the beams, the nozzles, the sound and the tool's
///     animation from <c>constructable</c>, which only the local player's building sets: handing it the partner's ghost
///     lets the game draw all of it as it does for its own player.
/// </summary>
public sealed partial class BuilderTool_LateUpdate_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((BuilderTool t) => t.LateUpdate());

    public static void Prefix(BuilderTool __instance)
    {
        if (!__instance.usingPlayer && RemoteToolEffects.TryGetBuildTarget(__instance, out Constructable target))
        {
            __instance.constructable = target;
        }
    }
}
