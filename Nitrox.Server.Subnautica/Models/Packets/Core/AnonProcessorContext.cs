using System.Net;
using Nitrox.Model.Core;
using Nitrox.Model.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Core;

/// <summary>
///     Context used by <see cref="IAuthPacketProcessor{TPacket}" />.
/// </summary>
internal record AnonProcessorContext : IPacketProcessContext<(SessionId SessionId, IPEndPoint EndPoint)>
{
    private readonly IPacketSender packetSender;
    public (SessionId SessionId, IPEndPoint EndPoint) Sender { get; set; }

    public AnonProcessorContext((SessionId SessionId, IPEndPoint EndPoint) sender, IPacketSender packetSender)
    {
        this.packetSender = packetSender;
        Sender = sender;
    }

    /// <summary>
    ///     True if this context sends through <paramref name="sender" />. Pooled contexts are shared by every server in the
    ///     process (tests host several), so they must not be reused for another one.
    /// </summary>
    public bool SendsThrough(IPacketSender sender) => ReferenceEquals(packetSender, sender);

    public async Task ReplyAsync<T>(T packet) where T : Packet => await packetSender.SendPacketAsync(packet, Sender.SessionId);

    public async Task SendToAllAsync<T>(T packet) where T : Packet => await packetSender.SendPacketToAllAsync(packet);
}
