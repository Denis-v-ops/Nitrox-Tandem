using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     The setter of <see cref="ConstructorBuildBot.launch" /> plays or stops <c>assignedHoverLoopSound</c>, which only the bot's
///     <c>Start()</c> assigns, and <c>Constructor.Update</c> sets it on every bot each frame. When another player deploys a
///     Mobile Vehicle Bay, the deploy runs from packet processing (<c>ConstructorMetadataProcessor</c>), so the bots
///     <c>Constructor.Deploy</c> creates may meet <c>Constructor.Update</c> before their <c>Start()</c> and the setter throws
///     (MULTINAUGHTICA_SPEC M1). Skipping the call loses nothing: <c>Start()</c> ends with <c>launch = false</c>, and the next
///     frame sets the real value.
/// </summary>
public sealed partial class ConstructorBuildBot_launch_set_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Property((ConstructorBuildBot t) => t.launch).SetMethod;

    public static bool Prefix(ConstructorBuildBot __instance)
    {
        return __instance.assignedHoverLoopSound;
    }
}
