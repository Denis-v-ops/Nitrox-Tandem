using System.Runtime.CompilerServices;
using HarmonyLib;
using NitroxPatcher.Patches.Dynamic;
using UnityObject = UnityEngine.Object;

namespace Nitrox.Test.Patcher.Patches.Dynamic;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: a build bot that hasn't run <c>Start()</c> (no hover sound yet) skips the launch setter, a started
///     one doesn't. Unity's null check is managed code over the native pointer field (<c>m_CachedPtr</c>), so the objects are
///     made without the engine. Whether the game really calls the setter before <c>Start()</c> can only be seen in game.
/// </summary>
[TestClass]
public class ConstructorBuildBot_launch_set_PatchTest
{
    private static readonly FieldInfo hoverSoundField = AccessTools.Field(typeof(ConstructorBuildBot), "assignedHoverLoopSound");

    [TestMethod]
    public void TargetIsTheLaunchSetter_AndTheHoverSoundFieldExists()
    {
        ConstructorBuildBot_launch_set_Patch.TARGET_METHOD.Name.Should().Be("set_launch");
        ConstructorBuildBot_launch_set_Patch.TARGET_METHOD.DeclaringType.Should().Be(typeof(ConstructorBuildBot));
        hoverSoundField.Should().NotBeNull();
        hoverSoundField.FieldType.Should().Be(typeof(FMOD_CustomLoopingEmitter));
    }

    [TestMethod]
    public void BotWithoutHoverSound_SkipsTheSetter()
    {
        ConstructorBuildBot bot = CreateBot(null);

        ConstructorBuildBot_launch_set_Patch.Prefix(bot).Should().BeFalse();
    }

    [TestMethod]
    public void BotWithDestroyedHoverSound_SkipsTheSetter()
    {
        ConstructorBuildBot bot = CreateBot(CreateEmitter(IntPtr.Zero));

        ConstructorBuildBot_launch_set_Patch.Prefix(bot).Should().BeFalse("Unity treats a destroyed object as null");
    }

    [TestMethod]
    public void StartedBot_RunsTheSetter()
    {
        ConstructorBuildBot bot = CreateBot(CreateEmitter(new IntPtr(1)));

        ConstructorBuildBot_launch_set_Patch.Prefix(bot).Should().BeTrue();
    }

    private static ConstructorBuildBot CreateBot(FMOD_CustomLoopingEmitter hoverSound)
    {
        ConstructorBuildBot bot = (ConstructorBuildBot)RuntimeHelpers.GetUninitializedObject(typeof(ConstructorBuildBot));
        hoverSoundField.SetValue(bot, hoverSound);
        return bot;
    }

    private static FMOD_CustomLoopingEmitter CreateEmitter(IntPtr nativePointer)
    {
        FieldInfo cachedPointer = AccessTools.Field(typeof(UnityObject), "m_CachedPtr");
        FMOD_CustomLoopingEmitter emitter = (FMOD_CustomLoopingEmitter)RuntimeHelpers.GetUninitializedObject(typeof(FMOD_CustomLoopingEmitter));
        cachedPointer.SetValue(emitter, nativePointer);
        return emitter;
    }
}
