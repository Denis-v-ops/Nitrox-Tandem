using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Gui.HUD;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerStatsProcessor(PlayerManager playerManager, SignalInterference signalInterference) : IClientPacketProcessor<PlayerStats>
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly SignalInterference signalInterference = signalInterference;

    public Task Process(ClientProcessorContext context, PlayerStats playerStats)
    {
        // Kept even before the partner's body exists, so the first ping already knows it.
        signalInterference.SetPartnerInterfered(playerStats.SessionId, playerStats.Interfered);
        if (playerManager.TryFind(playerStats.SessionId, out RemotePlayer remotePlayer))
        {
            RemotePlayerVitals vitals = remotePlayer.vitals;
            if (vitals)
            {
                vitals.SetOxygen(playerStats.Oxygen, playerStats.MaxOxygen);
                vitals.SetHealth(playerStats.Health);
                vitals.SetFood(playerStats.Food);
                vitals.SetWater(playerStats.Water);
            }
            remotePlayer.UpdateHealthAndInfection(playerStats.Health, playerStats.InfectionAmount);
        }
        return Task.CompletedTask;
    }
}
