using Nitrox.Model.GameLogic.FMOD;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Partner;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Buddy breathing (COOP_FEATURES_SPEC T2): air a partner gave us, already scaled by the server.
/// </summary>
internal sealed class OxygenShareProcessor(LocalPlayer localPlayer, FMODWhitelist fmodWhitelist) : IClientPacketProcessor<OxygenShare>
{
    private readonly LocalPlayer localPlayer = localPlayer;
    private readonly FMODWhitelist fmodWhitelist = fmodWhitelist;

    public Task Process(ClientProcessorContext context, OxygenShare packet)
    {
        if (packet.TargetId != localPlayer.SessionId || !Player.main || !(packet.Amount > 0))
        {
            return Task.CompletedTask;
        }
        Player.main.oxygenMgr.AddOxygen(packet.Amount);
        BuddyBreathingAction.PlayBubbles(fmodWhitelist, Player.main.transform.position, Player.main.gameObject);
        return Task.CompletedTask;
    }
}
