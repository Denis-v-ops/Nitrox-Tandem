using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Announces the local player's undocking when the game starts it: <c>DockedVehicleHandTarget.OnStartCinematicMode</c> calls
///     this only after <c>CinematicModeTriggerBase.StartCinematicMode</c> accepted the click (no other cinematic running, the held
///     tool put away). It used to be sent when the lock was granted, before the game decided, so a refused click opened the
///     partner's bay for a vehicle that never left (MULTINAUGHTICA_SPEC M1). The partner replays the start in
///     <c>VehicleUndockingProcessor</c>, which doesn't call this method.
/// </summary>
public sealed partial class VehicleDockingBay_OnUndockingStart_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((VehicleDockingBay t) => t.OnUndockingStart());

    public static void Prefix(VehicleDockingBay __instance)
    {
        Vehicle vehicle = __instance.GetDockedVehicle();
        if (!vehicle || !__instance.TryGetIdOrWarn(out NitroxId dockId) || !vehicle.TryGetIdOrWarn(out NitroxId vehicleId))
        {
            return;
        }

        Vehicles.EngagePlayerMovementSuppressor(vehicle);
        Resolve<IPacketSender>().Send(new VehicleUndocking(vehicleId, dockId, Resolve<IMultiplayerSession>().Reservation.SessionId, true));
    }
}
