using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The server saves: the game's own "Saving game" indicator shows it (<c>SavingIndicator_Update_Patch</c>). Where no
///     indicator runs in the play scene, the game's notice shows the same text once per save.
/// </summary>
internal sealed class ServerSavingProcessor(ServerSaveIndicator indicator) : IClientPacketProcessor<ServerSaving>
{
    private readonly ServerSaveIndicator indicator = indicator;
    private bool loggedIndicatorPath;
    private bool loggedNoticePath;

    public Task Process(ClientProcessorContext context, ServerSaving packet)
    {
        if (packet.Phase != ServerSaving.SavePhase.STARTED)
        {
            indicator.SaveEnded(packet.Number, Time.unscaledTime);
            if (packet.Phase == ServerSaving.SavePhase.FAILED)
            {
                Log.Warn($"[{nameof(ServerSaveIndicator)}] Server save {packet.Number} FAILED, see the server log");
            }
            else
            {
                Log.Debug($"[{nameof(ServerSaveIndicator)}] Server save {packet.Number} {packet.Phase}");
            }
            return Task.CompletedTask;
        }

        bool inGame = Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted;
        switch (indicator.SaveStarted(packet.Number, Time.unscaledTime, Time.frameCount, inGame))
        {
            case ServerSaveIndicator.Display.INDICATOR:
                if (!loggedIndicatorPath)
                {
                    loggedIndicatorPath = true;
                    Log.Info($"[{nameof(ServerSaveIndicator)}] Server saves show through the game's SavingIndicator");
                }
                break;
            case ServerSaveIndicator.Display.NOTICE when Language.main:
                if (!loggedNoticePath)
                {
                    loggedNoticePath = true;
                    Log.Info($"[{nameof(ServerSaveIndicator)}] No SavingIndicator runs in the play scene: server saves show as the game's notice");
                }
                ErrorMessage.AddMessage(Language.main.Get(SavingIndicator.savingGameMessage));
                Log.Info($"[{nameof(ServerSaveIndicator)}] Showed the notice for server save {packet.Number}");
                break;
            default:
                Log.Debug($"[{nameof(ServerSaveIndicator)}] Server save {packet.Number} started while loading: nothing to show it on yet");
                break;
        }
        return Task.CompletedTask;
    }
}
