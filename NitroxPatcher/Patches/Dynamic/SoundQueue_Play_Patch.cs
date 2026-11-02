#if DEBUG
using System.Reflection;
using NitroxClient.Debuggers.Bridge;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Dev bridge <c>sounds</c> (<see cref="BridgeSounds" />): records the PDA voice lines (<c>PDASounds.queue</c>) as
///     they start. The path is the sound's id or path as queued.
/// </summary>
public sealed partial class SoundQueue_Play_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((SoundQueue t) => t.Play(default, default));

    public static void Postfix(string sound, string subtitles)
    {
        BridgeSounds.Record("SoundQueue.Play", string.IsNullOrEmpty(subtitles) ? sound : $"{sound} [{subtitles}]", Player.main ? Player.main.transform.position : (UnityEngine.Vector3?)null);
    }
}
#endif
