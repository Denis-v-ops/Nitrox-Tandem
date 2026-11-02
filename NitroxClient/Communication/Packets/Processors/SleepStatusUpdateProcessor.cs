using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class SleepStatusUpdateProcessor(SleepManager sleepManager) : IClientPacketProcessor<SleepStatusUpdate>
{
    private readonly SleepManager sleepManager = sleepManager;

    public Task Process(ClientProcessorContext context, SleepStatusUpdate packet)
    {
        if (packet.PlayersInBed > 0)
        {
            string template = HasAwakePlayers(packet) ? Language.main.Get("Nitrox_SleepingPlayersWaitingFor") : Language.main.Get("Nitrox_SleepingPlayers");
            Log.InGame(FormatStatus(template, packet));
        }

        if (packet.AllPlayersInBed)
        {
            sleepManager.OnAllPlayersSleeping();
        }
        return Task.CompletedTask;
    }

    internal static bool HasAwakePlayers(SleepStatusUpdate packet) => packet.AwakePlayerNames is { Length: > 0 };

    /// <summary>
    ///     Fills in a status text such as "{SLEEPING}/{TOTAL} players sleeping. Waiting for {PLAYERS}…".
    /// </summary>
    internal static string FormatStatus(string template, SleepStatusUpdate packet)
    {
        string awakePlayers = HasAwakePlayers(packet) ? string.Join(", ", packet.AwakePlayerNames) : string.Empty;
        return template.Replace("{SLEEPING}", packet.PlayersInBed.ToString())
                       .Replace("{TOTAL}", packet.TotalPlayers.ToString())
                       .Replace("{PLAYERS}", awakePlayers);
    }
}
