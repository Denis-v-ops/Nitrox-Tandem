using Nitrox.Model.Helper;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerDeathProcessor(PlayerManager playerManager, RescueWindow rescueWindow) : IClientPacketProcessor<PlayerDeathEvent>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly RescueWindow rescueWindow = rescueWindow;

    public Task Process(ClientProcessorContext context, PlayerDeathEvent playerDeath)
    {
        RemotePlayer player = Validate.IsPresent(playerManager.Find(playerDeath.SessionId));
        Log.Debug($"{player.PlayerName} died");
        Log.InGame(Language.main.Get("Nitrox_PlayerDied").Replace("{PLAYER}", player.PlayerName));
        // A downed partner's pose and distress call end with their death (COOP_FEATURES_SPEC T1, D3)
        rescueWindow.OnPartnerDied(player);
        player.PlayerDeathEvent.Trigger(player);
        return Task.CompletedTask;

        // TODO: Add any death related triggers (i.e. scoreboard updates, rewards, etc.)
    }
}
