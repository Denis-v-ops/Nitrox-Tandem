using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A teleporter whose pair's story goal is complete starts open (<see cref="TeleporterPairs" />, MULTINAUGHTICA_SPEC M2):
///     <c>Start</c> opens it when <c>TeleporterManager.GetTeleporterActive</c> says so, and the game's set is empty after a
///     join. Patched here rather than in <c>GetTeleporterActive</c>, which is small enough for Mono to inline.
/// </summary>
public sealed partial class PrecursorTeleporter_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorTeleporter t) => t.Start());

    public static void Prefix(PrecursorTeleporter __instance)
    {
        TeleporterPairs.RestoreFromGoal(__instance.teleporterIdentifier);
    }
}
