using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     A vehicle's lights use power every frame on every game that has the vehicle loaded, and each game's whole-number battery
///     updates overwrite the other's: when the two copies move in opposite directions (the holder's copy recharging in a
///     moonpool, the partner's copy draining for the lights) the charge sticks at a whole number. Only the vehicle's lock
///     holder runs it, like <see cref="Vehicle_UpdateEnergyRecharge_Patch" />; the partner gets the charge through
///     <c>BatteryMetadata</c> (MULTINAUGHTICA_SPEC M1). Vehicles are global root entities, so a connected player always holds
///     their lock. Lights of tools and anything that isn't part of a vehicle keep the game's behaviour.
/// </summary>
public sealed partial class ToggleLights_UpdateLightEnergy_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((ToggleLights t) => t.UpdateLightEnergy());

    public static bool Prefix(ToggleLights __instance)
    {
        Vehicle vehicle = __instance.GetComponentInParent<Vehicle>();
        return !vehicle || !vehicle.TryGetNitroxId(out NitroxId id) || Resolve<SimulationOwnership>().HasAnyLockType(id);
    }
}
