#if DEBUG
using System.Reflection;
using NitroxClient.Debuggers.Bridge;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Dev bridge <c>sounds</c> (<see cref="BridgeSounds" />): records emitters started with <c>StartEvent</c> (the
///     player's breathing loop and pain sounds, among others). Nitrox doesn't sync this method.
/// </summary>
public sealed partial class FMOD_StudioEventEmitter_StartEvent_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((FMOD_StudioEventEmitter t) => t.StartEvent());

    public static void Postfix(FMOD_StudioEventEmitter __instance)
    {
        BridgeSounds.Record("StudioEventEmitter.StartEvent", __instance.asset ? __instance.asset.path : __instance.path, __instance);
    }
}
#endif
