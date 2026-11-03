using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S15d: only the game that simulates a bleeder lets it pick a target. A copy that the local player touched
///     marked the player's own attach target occupied, and nothing cleared it: the copy never starts its attach
///     (<c>StartPerform</c> runs only where the bleeder is simulated), so the bleeders this game simulates couldn't latch
///     on any more while that copy was loaded.
/// </summary>
public sealed partial class AttachAndSuck_OnCollisionEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.OnCollisionEnter(default));

    public static bool Prefix(AttachAndSuck __instance)
    {
        return Resolve<Bleeders>().IsSimulatedHere(__instance);
    }
}
