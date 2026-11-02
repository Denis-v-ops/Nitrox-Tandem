#if DEBUG
using System.Reflection;
using FMODUnity;
using NitroxClient.Debuggers.Bridge;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Dev bridge <c>sounds</c> (<see cref="BridgeSounds" />): records FMODUnity's own emitter (e.g. the light pain sound
///     of <c>PlayerDamageSounds</c>).
/// </summary>
public sealed partial class StudioEventEmitter_Play_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((StudioEventEmitter t) => t.Play());

    public static void Postfix(StudioEventEmitter __instance)
    {
        BridgeSounds.Record("FMODUnity.StudioEventEmitter.Play", __instance.Event, __instance);
    }
}
#endif
