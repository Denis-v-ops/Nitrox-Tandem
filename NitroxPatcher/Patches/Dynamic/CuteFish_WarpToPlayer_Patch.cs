using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC P1: every 5 s the game warps a following cuddlefish to within 40 m of the local player
///     (<c>InvokeRepeating</c>, which the creature AI sync doesn't cover). Only the game that simulates it may do that;
///     before, each game's copy jumped to its own player, so the partner saw a cuddlefish follow them that wasn't there.
/// </summary>
public sealed partial class CuteFish_WarpToPlayer_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CuteFish t) => t.WarpToPlayer());

    public static bool Prefix(CuteFish __instance)
    {
        return Resolve<Cuddlefish>().IsSimulatedHere(__instance);
    }
}
