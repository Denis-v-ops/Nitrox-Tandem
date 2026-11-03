using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S15d: the detach rules the game checks only for its own player, for a partner's body and a downed
///     player (see <see cref="Bleeders.AfterUpdate" />).
/// </summary>
public sealed partial class AttachAndSuck_Update_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.Update());

    public static void Postfix(AttachAndSuck __instance)
    {
        if (__instance.active && __instance.currentTarget)
        {
            Resolve<Bleeders>().AfterUpdate(__instance);
        }
    }
}
