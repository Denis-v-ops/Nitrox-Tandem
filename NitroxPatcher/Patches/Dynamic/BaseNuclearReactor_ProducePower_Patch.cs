using System.Reflection;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Only one game uses up the rods of a nuclear reactor (COOP_FEATURES_SPEC B3): the one holding the base's simulation lock
/// (<see cref="BaseModuleSimulation" />), like the bioreactor (<see cref="BaseBioReactor_ProducePower_Patch" />).
/// </summary>
/// <remarks>
/// <c>BaseNuclearReactor.ProducePower</c> (game 83031, called from <c>Update</c> in every game with the base loaded) adds
/// the requested energy to <c>_toConsume</c>; when that reaches a rod's 20000 it takes the rod out
/// (<c>equipment.RemoveItem</c>, sent by <c>Equipment_RemoveItem_Patch</c> as <c>EntityDestroyed</c>), destroys it and
/// creates a depleted rod in the slot (<c>AddDepletedRodToEquipmentAsync</c>: <c>Pickupable.Pickup</c> and
/// <c>equipment.AddItem</c>, both sent). Without this, every game did that with its own new depleted rod. The other games
/// return the requested energy while a rod is active (their base stays powered; the producer's value replaces theirs, see
/// <see cref="BasePowerSync" />) and wait for the producer's rod changes. <c>_toConsume</c> (the energy taken from the
/// current rod so far) is neither synced nor saved by Nitrox, as before: after a restart, or when the lock moves to
/// another game, the count starts again from that game's value, so a rod can last up to one rod's worth longer.
///
/// </remarks>
public sealed partial class BaseNuclearReactor_ProducePower_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((BaseNuclearReactor t) => t.ProducePower(default));

    public static bool Prefix(BaseNuclearReactor __instance, float requested, ref float __result)
    {
        if (Resolve<BaseModuleSimulation>().IsLocalProducer(__instance))
        {
            return true;
        }

        __result = requested > 0f && __instance.CountActiveRods() > 0 ? requested : 0f;
        return false;
    }
}
