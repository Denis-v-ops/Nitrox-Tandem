using Nitrox.Model.Core;
using Nitrox.Model.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Core;

/// <summary>
///     Context used by <see cref="IAuthPacketProcessor{TPacket}" />.
/// </summary>
internal record AuthProcessorContext : IPacketProcessContext<Player>
{
    private readonly IPacketSender packetSender;
    public Player Sender { get; set; }

    public AuthProcessorContext(Player sender, IPacketSender packetSender)
    {
        this.packetSender = packetSender;
        Sender = sender;
    }

    public async Task SendAsync<T>(T packet, SessionId sessionId) where T : Packet => await packetSender.SendPacketAsync(packet, sessionId);

    /// <summary>
    ///     True if this context sends through <paramref name="sender" />. Pooled contexts are shared by every server in the
    ///     process (tests host several), so they must not be reused for another one.
    /// </summary>
    public bool SendsThrough(IPacketSender sender) => ReferenceEquals(packetSender, sender);

    public async Task ReplyAsync<T>(T packet) where T : Packet => await packetSender.SendPacketAsync(packet, Sender.SessionId);

    public async Task SendToAllAsync<T>(T packet) where T : Packet => await packetSender.SendPacketToAllAsync(packet);

    public async Task SendToOthersAsync<T>(T packet) where T : Packet => await packetSender.SendPacketToOthersAsync(packet, Sender.SessionId);
}
