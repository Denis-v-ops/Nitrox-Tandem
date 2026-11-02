using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     M4: another game's copy of a Reaper grabbed a vehicle, and this game's copy grabs the same one. Each copy touches
///     vehicles on its own (the Reaper's aggression is synced, <c>AI.syncedCreatureWhitelist</c>), so before, only one game
///     showed the vehicle held while the other showed the pilot driving free. The pilot's game, which holds the vehicle's
///     lock, also applies the grip's damage (<c>ReaperLeviathan.DamageVehicle</c> passes no dealer, other games drop it).
/// </summary>
internal sealed class ReaperGrabVehicleProcessor : IClientPacketProcessor<ReaperGrabVehicle>
{
    /// <summary>
    ///     A copy that grabbed because another game said so doesn't let go on its own timer (the game's random 8 to 13 s
    ///     would differ in each game): the game that grabbed first says when. This is only the fallback for a release that
    ///     never comes (that game left or unloaded the Reaper).
    /// </summary>
    internal const float FALLBACK_RELEASE_SECONDS = 20f;

    public Task Process(ClientProcessorContext context, ReaperGrabVehicle packet)
    {
        if (!NitroxEntity.TryGetComponentFrom(packet.ReaperId, out ReaperLeviathan reaper) ||
            !NitroxEntity.TryGetComponentFrom(packet.VehicleId, out Vehicle vehicle))
        {
            return Task.CompletedTask;
        }

        // It grabbed on its own at the same moment, or it is dead here (a dead Reaper is disabled: it would hold the
        // vehicle without moving it)
        if (reaper.IsHoldingVehicle() || !reaper.isActiveAndEnabled || (reaper.liveMixin && !reaper.liveMixin.IsAlive()))
        {
            return Task.CompletedTask;
        }

        using (PacketSuppressor<ReaperGrabVehicle>.Suppress())
        {
            switch (vehicle)
            {
                case SeaMoth seamoth when !seamoth.docked:
                    reaper.GrabSeamoth(seamoth);
                    break;
                case Exosuit exosuit when !exosuit.docked:
                    reaper.GrabExosuit(exosuit);
                    break;
                default:
                    return Task.CompletedTask;
            }
        }

        reaper.CancelInvoke(nameof(ReaperLeviathan.ReleaseVehicle));
        reaper.Invoke(nameof(ReaperLeviathan.ReleaseVehicle), FALLBACK_RELEASE_SECONDS);
        return Task.CompletedTask;
    }
}
