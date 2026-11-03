using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S15d: a bleeder lets go. The simulating game tells the others; a copy on the local player (knocked off,
///     or the player went inside or down) tells the simulating game. A copy also ends its mirrored attach here, as
///     <c>StopPerform</c> does where the bleeder is simulated.
/// </summary>
public sealed partial class AttachAndSuck_SetDetached_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.SetDetached());

    public static void Prefix(AttachAndSuck __instance)
    {
        Resolve<Bleeders>().OnDetaching(__instance);
    }

    public static void Postfix(AttachAndSuck __instance)
    {
        if (!Resolve<Bleeders>().IsSimulatedHere(__instance))
        {
            __instance.active = false;
            Bleeders.SetFollowingRemotePoses(__instance, true);
        }
    }
}
