using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC G1: a player who walks out of the gun terminal's area withdraws their click, so the partner's
///     click doesn't start a cinematic for someone who left.
/// </summary>
public sealed partial class PrecursorDisableGunTerminal_OnTerminalAreaExit_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorDisableGunTerminal t) => t.OnTerminalAreaExit());

    public static void Prefix()
    {
        Resolve<StoryGates>().Cancel(StoryGateKind.DISABLE_GUN);
    }
}
