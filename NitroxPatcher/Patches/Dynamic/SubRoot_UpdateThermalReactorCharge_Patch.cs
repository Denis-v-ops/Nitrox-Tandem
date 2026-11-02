using System.Linq;
using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     The thermal reactor adds energy every frame on every game where the Cyclops' <c>SubRoot.Update</c> runs (LOD not Minimal),
///     and each game's whole-number battery updates overwrite the other's. While another player pilots, their game drains
///     the engine and the partner's game only charged: the partner's copy won, so driving cost nothing, or the charge stuck at
///     a whole number. So while someone else pilots, only the pilot's game (it holds the Cyclops' lock and is aboard) runs the
///     reactor (MULTINAUGHTICA_SPEC M1). When nobody pilots, every game near it charges in the same direction, as before: the
///     lock follows the last pilot, not the players aboard, so "only the lock holder" stopped the charge when the last pilot
///     was far away while the partner stayed aboard.
/// </summary>
public sealed partial class SubRoot_UpdateThermalReactorCharge_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.UpdateThermalReactorCharge());

    public static bool Prefix(SubRoot __instance)
    {
        if (!__instance.thermalReactorUpgrade || !__instance.TryGetNitroxId(out NitroxId id) || Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            return true;
        }
        return !Resolve<PlayerManager>().GetAll().Any(remotePlayer => remotePlayer.SubRoot == __instance && remotePlayer.PilotingChair);
    }
}
