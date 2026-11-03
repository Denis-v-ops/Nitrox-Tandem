using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

internal sealed class SleepManager(IPacketSender packetSender, PlayerManager playerManager, TimeService timeService) : ISessionCleaner
{
    /// <summary>Duration of the sleep animation/screen fade in seconds.</summary>
    private const float SLEEP_DURATION = 5f;
    /// <summary>Time to skip when sleeping. From Bed.kSleepEndTime - Bed.kSleepStartTime (1188 - 792 = 396).</summary>
    private const float SLEEP_TIME_SKIP_SECONDS = 396f;

    private readonly IPacketSender packetSender = packetSender;
    private readonly TimeService timeService = timeService;
    private readonly ThreadSafeSet<SessionId> sessionIdsInBed = [];
    private bool isSleepInProgress;
    private readonly PlayerManager playerManager = playerManager;

    public async Task PlayerEnteredBed(Player player)
    {
        if (!sessionIdsInBed.Add(player.SessionId))
        {
            return;
        }

        await BroadcastStatus();
        if (!isSleepInProgress && AreAllPlayersInBed())
        {
            await StartSleep();
        }
    }

    public async Task PlayerExitedBed(Player player)
    {
        if (!sessionIdsInBed.Remove(player.SessionId))
        {
            return;
        }

        await BroadcastStatus();
    }

    private bool AreAllPlayersInBed()
    {
        int totalPlayers = playerManager.GetConnectedPlayers().Count;
        return totalPlayers > 0 && sessionIdsInBed.Count >= totalPlayers;
    }

    private async Task BroadcastStatus()
    {
        await packetSender.SendPacketToAllAsync(CreateStatus(playerManager.GetConnectedPlayers()));
    }

    /// <param name="connectedPlayers">Everyone who counts for the sleep, without a player who is just leaving.</param>
    private SleepStatusUpdate CreateStatus(List<Player> connectedPlayers)
    {
        string[] awakePlayerNames = connectedPlayers.Where(player => !sessionIdsInBed.Contains(player.SessionId))
                                                    .Select(player => player.Name)
                                                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                                    .ToArray();
        return new SleepStatusUpdate(sessionIdsInBed.Count, connectedPlayers.Count, awakePlayerNames);
    }

    private async Task StartSleep()
    {
        isSleepInProgress = true;
        SessionId[] sleepers = sessionIdsInBed.ToArray();
        sessionIdsInBed.Clear();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(SLEEP_DURATION));
            await timeService.SkipTimeAsync(TimeSpan.FromSeconds(SLEEP_TIME_SKIP_SECONDS));
            await packetSender.SendPacketToAllAsync(new SleepComplete());
            // A completed sleep is a rest: the rescue window is full again (COOP_FEATURES_SPEC T1)
            foreach (SessionId sleeper in sleepers)
            {
                if (playerManager.TryGetPlayerBySessionId(sleeper, out Player? player))
                {
                    player.DownsSinceRest = 0;
                }
            }
        } finally
        {
            isSleepInProgress = false;
        }
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        bool leaverWasInBed = sessionIdsInBed.Remove(args.Session.Id);
        // If sleep is already in progress, let it complete - don't cancel just because someone disconnected
        if (isSleepInProgress)
        {
            return;
        }
        // Nobody is waiting for anything
        if (!leaverWasInBed && sessionIdsInBed.Count <= 0)
        {
            return;
        }
        // The session cleaners run one after another in no fixed order, so PlayerManager may still list the leaving player
        List<Player> remainingPlayers = playerManager.GetConnectedPlayersExcept(args.Session.Id);
        if (remainingPlayers.Count <= 0)
        {
            return;
        }

        // Send to all players except the disconnecting one. Also when the only sleeper left, so the others' status
        // no longer counts them in bed.
        await packetSender.SendPacketToOthersAsync(CreateStatus(remainingPlayers), args.Session.Id);

        // Check if remaining players are now all sleeping (disconnected player was the only one awake)
        if (sessionIdsInBed.Count > 0 && sessionIdsInBed.Count >= remainingPlayers.Count)
        {
            await StartSleep();
        }
    }
}
