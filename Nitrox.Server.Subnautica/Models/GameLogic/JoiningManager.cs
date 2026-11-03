using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic.Bases;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

// TODO: Refactor this to a QueuingBackgroundService to simplify state tracking.
internal sealed class JoiningManager(
    IPacketSender packetSender,
    PlayerManager playerManager,
    SessionManager sessionManager,
    WorldEntityManager worldEntityManager,
    PdaManager pdaManager,
    StoryManager storyManager,
    StoryScheduler storyScheduler,
    EntitySimulation entitySimulation,
    EscapePodManager escapePodManager,
    EntityRegistry entityRegistry,
    SessionSettings sessionSettings,
    RescueService rescueService,
    IOptions<SubnauticaServerOptions> options,
    ILogger<JoiningManager> logger)
    : ISessionCleaner
{
    private readonly IPacketSender packetSender = packetSender;
    private readonly PlayerManager playerManager = playerManager;
    private readonly SessionManager sessionManager = sessionManager;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly PdaManager pdaManager = pdaManager;
    private readonly StoryManager storyManager = storyManager;
    private readonly StoryScheduler storyScheduler = storyScheduler;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<JoiningManager> logger = logger;
    private readonly EscapePodManager escapePodManager = escapePodManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly SessionSettings sessionSettings = sessionSettings;
    private readonly RescueService rescueService = rescueService;

    private readonly ThreadSafeQueue<SessionId> joinQueue = new();
    private readonly Lock queueLocker = new(); // Necessary to avoid race conditions between JoinQueueLoop and AddToJoinQueue
    private bool queueActive;
    public Action? SyncFinishedCallback { get; private set; }

    private async Task JoinQueueLoop()
    {
        while (true)
        {
            lock (queueLocker)
            {
                if (joinQueue.Count == 0)
                {
                    queueActive = false;
                    return;
                }
            }

            try
            {
                SessionId sessionId = joinQueue.Dequeue();
                string? name = playerManager.GetPlayerReservation(sessionId)?.PlayerName;
                if (name == null)
                {
                    continue;
                }

                // Do this after dequeuing because everyone's position shifts forward
                SessionId[] array = [.. joinQueue];
                for (int i = 0; i < array.Length; i++)
                {
                    SessionId s = array[i];
                    await packetSender.SendPacketAsync(new JoinQueueInfo(i + 1, options.Value.InitialSyncTimeout), s);
                }

                logger.ZLogInformation($"Starting sync for player {name}");
                await SendInitialSyncAsync(sessionId);

                using CancellationTokenSource source = new(options.Value.InitialSyncTimeout);
                bool syncFinished = false;

                SyncFinishedCallback = () => { syncFinished = true; };

                while (!syncFinished && sessionManager.IsConnected(sessionId) && !source.IsCancellationRequested)
                {
                    await Task.Delay(10, source.Token);
                }

                if (!sessionManager.IsConnected(sessionId))
                {
                    logger.ZLogInformation($"Player {name} disconnected while syncing");
                }
                else if (source.IsCancellationRequested)
                {
                    logger.ZLogInformation($"Initial sync timed out for player {name}");
                    SyncFinishedCallback = null;

                    if (sessionManager.IsConnected(sessionId))
                    {
                        await packetSender.SendPacketAsync(new PlayerKicked("Initial sync took too long and timed out"), sessionId);
                    }
                }
                else
                {
                    logger.ZLogInformation($"Player {name} joined successfully. Remaining requests: {joinQueue.Count}");
                    if (!playerManager.TryGetPlayerBySessionId(sessionId, out Player? player))
                    {
                        throw new Exception($"Failed to get player object for session #{sessionId}");
                    }
                    BroadcastPlayerJoined(player);
                    await ReplayOtherPlayersPoseAsync(player);
                    // Downed partners, and this player's own window if they quit while down (COOP_FEATURES_SPEC T1)
                    await rescueService.PlayerJoinedAsync(player);
                }
            }
            catch (Exception e)
            {
                logger.ZLogInformation($"Unexpected error during player connection inside the join queue: {e}");
            }
        }
    }

    public void AddToJoinQueue(SessionId sessionId)
    {
        // Necessary to avoid race conditions between JoinQueueLoop and AddToJoinQueue
        lock (queueLocker)
        {
            logger.ZLogInformation($"Added player {playerManager.GetPlayerReservation(sessionId)?.PlayerName} to queue");
            joinQueue.Enqueue(sessionId);

            if (queueActive)
            {
                packetSender.SendPacketAsync(new JoinQueueInfo(joinQueue.Count, options.Value.InitialSyncTimeout), sessionId);
            }
            else
            {
                // It may be possible to use the task's status itself for this,
                // but the ContinueWithHandleError callback might cause issues
                queueActive = true;
                Task.Run(JoinQueueLoop).ContinueWithHandleError();
            }
        }
    }

    private async Task SendInitialSyncAsync(SessionId sessionId)
    {
        Player player = playerManager.CreatePlayerData(sessionId, out bool wasBrandNewPlayer);
        (NitroxId assignedEscapePodId, EscapePodEntity? newlyCreatedEscapePod) = await escapePodManager.AssignPlayerToEscapePodAsync(player.PeerId);

        if (wasBrandNewPlayer)
        {
            player.SubRootId = assignedEscapePodId;
        }

        if (newlyCreatedEscapePod is { } validEscapePod)
        {
            SpawnEntities spawnNewEscapePod = new(validEscapePod);
            await packetSender.SendPacketToOthersAsync(spawnNewEscapePod, sessionId);
        }

        // TODO: Remove this code when security of player login is improved by https://github.com/SubnauticaNitrox/Nitrox/issues/1996
        // We need to reset permissions on join, otherwise players can impersonate an admin easily.
        player.Permissions = options.Value.DefaultPlayerPerm;

        // Make players on localhost admin by default.
        if (options.Value.LocalhostIsAdmin && sessionManager.GetEndPoint(sessionId)?.Address.IsLocalhost() == true)
        {
            logger.ZLogInformation($"Granted admin to '{player.Name}' because they're playing on the host machine");
            player.Permissions = Perms.ADMIN;
        }

        // Infection story goals (progress, cure) completed while this player was offline never ran in their game
        if (player.Stats is { } stats)
        {
            float infection = InfectionStory.Reconcile(stats.InfectionAmount, storyManager.StoryGoalData.CompletedGoals);
            if (infection != stats.InfectionAmount)
            {
                logger.ZLogInformation($"Infection of '{player.Name}' updated from {stats.InfectionAmount} to {infection} by story goals completed while they were offline");
                player.Stats = new PlayerStatsData(stats.Oxygen, stats.MaxOxygen, stats.Health, stats.Food, stats.Water, infection);
            }
        }

        List<SimulatedEntity> simulations = entitySimulation.AssignGlobalRootEntitiesAndGetData(player);

        player.Entity = wasBrandNewPlayer ? SetupNewPlayerEntity(player) : RespawnExistingEntity(player);

        // Position only changes with the player's own packets, but inside a Cyclops the player entity is a child of the
        // sub's entity and follows it. A passenger who left a Cyclops that the partner then drove on comes back inside it,
        // not in the water where it was (FIX_SPEC S13). Only for vehicles: they are the subs that move, and their
        // passengers' local positions are kept current (PlayerInCyclopsMovement, VehicleMovements for the pilot).
        if (!wasBrandNewPlayer && player.SubRootId.HasValue && player.Entity.Transform is { Parent: { } subTransform } &&
            entityRegistry.TryGetEntityById(player.SubRootId.Value, out VehicleEntity? sub) && sub.Transform == subTransform)
        {
            player.Position = player.Entity.Transform.Position;
            player.Rotation = player.Entity.Transform.Rotation;
        }

        List<GlobalRootEntity> globalRootEntities = worldEntityManager.GetGlobalRootEntities(true);
        bool isFirstPlayer = playerManager.GetConnectedPlayers().Count == 1;

        InitialPlayerSync initialPlayerSync = new(
            player.GameObjectId,
            wasBrandNewPlayer,
            assignedEscapePodId,
            player.EquippedItems,
            player.UsedItems,
            player.QuickSlotsBindingIds,
            pdaManager.GetInitialPDAData(OwnFirsts.UnheardBy(player.HeardGoals, playerManager.GetAllPlayers().Select(p => p.HeardGoals), options.Value.PartnerDiscoveriesSilent)),
            storyManager.GetInitialStoryGoalData(storyScheduler, player),
            player.Position,
            player.Rotation,
            player.SubRootId,
            player.Stats,
            GetOtherPlayers(player),
            globalRootEntities,
            simulations,
            player.GameMode,
            player.Permissions,
            wasBrandNewPlayer ? IntroCinematicMode.LOADING : IntroCinematicMode.COMPLETED,
            new(new(player.PingInstancePreferences), player.PinnedRecipePreferences.ToList()),
            storyManager.GetTimeData(),
            isFirstPlayer,
            BuildingManager.GetEntitiesOperations(globalRootEntities),
            options.Value.KeepInventoryOnDeath,
            sessionSettings,
            player.InPrecursor,
            player.DisplaySurfaceWater,
            options.Value.MarkDeathPointsWithBeacon,
            new CoopSettings
            {
                RescueWindowSeconds = options.Value.RescueWindowSeconds,
                PartnerSignalInterference = options.Value.PartnerSignalInterference,
                SignalInterferenceDepth = options.Value.SignalInterferenceDepth,
                PartnerDiscoveriesSilent = options.Value.PartnerDiscoveriesSilent,
                CraftFromLockers = options.Value.CraftFromLockers
            }
        );

        await packetSender.SendPacketAsync(initialPlayerSync, player.SessionId);

        IEnumerable<PlayerContext> GetOtherPlayers(Player player)
        {
            return playerManager.GetConnectedPlayers().Where(p => p != player).Select(p => p.PlayerContext);
        }

        PlayerEntity SetupNewPlayerEntity(Player player, bool hasPose = false)
        {
            NitroxTransform transform = new(player.Position, player.Rotation, NitroxVector3.One);
            // The entity's parent id is the escape pod, so its transform needs that parent too: movements are stored through
            // the world-pose setters, which convert into the parent's space, and a restart links the saved local pose under
            // the parent. Unparented, the saved pose was the world pose read as local (FIX_SPEC S13 finding, MULTINAUGHTICA_SPEC
            // M2). A brand-new player's Position is only the default (0, 0, 0), not a place: it starts at the pod's origin.
            if (player.SubRootId.HasValue && entityRegistry.TryGetEntityById(player.SubRootId.Value, out WorldEntity? parent) && parent.Transform != null)
            {
                transform.SetParent(parent.Transform, hasPose);
            }

            PlayerEntity playerEntity = new(transform, 0, null, false, player.GameObjectId, NitroxTechType.None, null, player.SubRootId.OrNull(), []);
            entityRegistry.AddOrUpdate(playerEntity);
            worldEntityManager.TrackEntityInTheWorld(playerEntity);
            return playerEntity;
        }

        PlayerEntity RespawnExistingEntity(Player player)
        {
            if (entityRegistry.TryGetEntityById(player.PlayerContext.PlayerNitroxId, out PlayerEntity playerWorldEntity))
            {
                return playerWorldEntity;
            }
            logger.ZLogError($"Unable to find player entity for {player.Name}. Re-creating one");
            return SetupNewPlayerEntity(player, true);
        }
    }

    private void BroadcastPlayerJoined(Player player)
    {
        PlayerJoinedMultiplayerSession playerJoinedPacket = new(player.PlayerContext, player.SubRootId, player.Entity);
        packetSender.SendPacketToOthersAsync(playerJoinedPacket, player.SessionId);
    }

    /// <summary>
    ///     Sends the joined player what the others are doing with their bodies: every animation state and the held item
    ///     (COOP_FEATURES_SPEC R2a). Sent after the initial sync, so the game has the partner's body and inventory and its
    ///     usual processors apply them. The first-use flag is left out, so the first-use animation doesn't play again.
    ///     Also their latest stats with the interference flag (D2).
    /// </summary>
    private async Task ReplayOtherPlayersPoseAsync(Player player)
    {
        foreach (Player other in playerManager.GetConnectedPlayers())
        {
            if (other == player)
            {
                continue;
            }

            foreach (KeyValuePair<AnimChangeType, AnimChangeState> animation in other.Animations.ToArray())
            {
                await packetSender.SendPacketAsync(new AnimationChangeEvent(other.SessionId, new PlayerAnimation(animation.Key, animation.Value)), player.SessionId);
            }

            if (other.HeldItem is { } heldItem)
            {
                await packetSender.SendPacketAsync(new PlayerHeldItemChanged(other.SessionId, heldItem.ItemId, heldItem.Type, null), player.SessionId);
            }

            // The partner's latest stats and signal interference (COOP_FEATURES_SPEC D2), so the marker is right before their
            // game's next PlayerStats (every 3 s)
            if (other.Stats is { } stats)
            {
                await packetSender.SendPacketAsync(new PlayerStats(other.SessionId, stats.Oxygen, stats.MaxOxygen, stats.Health, stats.Food, stats.Water, stats.InfectionAmount, other.Interfered),
                                                   player.SessionId);
            }
        }
    }

    public Task OnEventAsync(ISessionCleaner.Args args)
    {
        // They may have been queued, so just erase their entry
        joinQueue.RemoveWhere(sessionId => Equals(sessionId, args.Session.Id));
        return Task.CompletedTask;
    }
}
