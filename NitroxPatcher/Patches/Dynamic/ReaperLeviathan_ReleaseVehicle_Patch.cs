using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours.Vehicles;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Enables vehicle's position sync when they're released from reapers, and (M4) tells the other games that the Reaper let
/// go (see <see cref="ReaperLeviathan_GrabVehicle_Patch" />).
/// </summary>
public sealed partial class ReaperLeviathan_ReleaseVehicle_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((ReaperLeviathan t) => t.ReleaseVehicle());

    public static void Prefix(ReaperLeviathan __instance)
    {
        if (__instance.holdingVehicle && __instance.holdingVehicle.TryGetComponent(out VehicleMovementReplicator vehicleMovementReplicator))
        {
            vehicleMovementReplicator.enabled = true;
        }

        // Only while something is held: the game also calls this every frame while a destroyed vehicle is still "held"
        // (Update), and the others learn of that from the destruction. And only while the Reaper is active: its OnDisable
        // lets go too (dead, unloaded or destroyed in this game), which is no release for the other games to follow.
        if (__instance.isActiveAndEnabled && __instance.holdingVehicle && __instance.TryGetNitroxId(out NitroxId reaperId))
        {
            Resolve<IPacketSender>().Send(new ReaperReleaseVehicle(reaperId));
        }

        // The fallback timer of a grab another game announced must not let go of the next grab
        __instance.CancelInvoke(nameof(ReaperLeviathan.ReleaseVehicle));
    }
}
