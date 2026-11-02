using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A Precursor teleporter pair was activated in this game (its terminal, or a teleporter opening): stored as a story goal
///     so the pair stays active for everyone (<see cref="TeleporterPairs" />, MULTINAUGHTICA_SPEC M2).
/// </summary>
public sealed partial class TeleporterManager_ActivateTeleporter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => TeleporterManager.ActivateTeleporter(default));

    public static void Postfix(string identifier)
    {
        TeleporterPairs.ReportActivation(identifier);
    }
}
