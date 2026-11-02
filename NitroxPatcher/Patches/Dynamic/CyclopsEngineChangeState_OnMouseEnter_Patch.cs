using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     No "engine on/off" hint for a crew member who isn't piloting: only the pilot can use the engine button
///     (<see cref="CyclopsEngineChangeState_OnClick_Patch" />).
/// </summary>
public sealed partial class CyclopsEngineChangeState_OnMouseEnter_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsEngineChangeState t) => t.OnMouseEnter());

    public static bool Prefix()
    {
        return Player.main.isPiloting;
    }
}
