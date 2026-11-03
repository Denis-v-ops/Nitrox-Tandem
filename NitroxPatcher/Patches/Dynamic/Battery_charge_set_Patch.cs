using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Sends a battery's charge when it changes (see <see cref="BatteryChargeRules" />). A prefix, so the old value is known, but
///     it sends the value the setter is about to store: extracting the metadata here read the old value, so the last step
///     (a battery running empty) never arrived (MULTINAUGHTICA_SPEC M1).
/// </summary>
public sealed partial class Battery_charge_set_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Property((Battery t) => t.charge).SetMethod;

    public static void Prefix(Battery __instance, float value)
    {
        float newCharge = BatteryChargeRules.StoredCharge(value, __instance._capacity);
        if (BatteryChargeRules.ShouldSend(__instance._charge, newCharge) && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<Entities>().BroadcastMetadataUpdate(id, new BatteryMetadata(newCharge));
        }
    }
}
