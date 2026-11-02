using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     M4: another game's copy of a Reaper let go of its vehicle, and this game's copy lets go too
///     (see <see cref="ReaperGrabVehicleProcessor" />).
/// </summary>
internal sealed class ReaperReleaseVehicleProcessor : IClientPacketProcessor<ReaperReleaseVehicle>
{
    public Task Process(ClientProcessorContext context, ReaperReleaseVehicle packet)
    {
        if (!NitroxEntity.TryGetComponentFrom(packet.ReaperId, out ReaperLeviathan reaper) || !reaper.IsHoldingVehicle())
        {
            return Task.CompletedTask;
        }

        using (PacketSuppressor<ReaperReleaseVehicle>.Suppress())
        {
            reaper.ReleaseVehicle();
        }
        return Task.CompletedTask;
    }
}
