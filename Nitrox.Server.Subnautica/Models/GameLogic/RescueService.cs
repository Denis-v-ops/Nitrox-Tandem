using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The rescue window (COOP_FEATURES_SPEC T1): the server owns who is down and for how long. A player's game sends
///     <see cref="PlayerDowned" /> instead of dying; this service grants a window (<see cref="RescueRules" />, escalation by
///     <see cref="SubnauticaServerOptions.RescueEscalation" />) or refuses it with <see cref="PlayerRescueExpired" />, accepts
///     revives from a partner who can reach them, and lets the window run out.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Player.DownedUntil" /> is a time of <see cref="TimeService.ActiveTime" />. The timer pauses while the
///         downed player is offline: every tick moves their <see cref="Player.DownedUntil" /> on by the time that passed, so a
///         player who quits while down comes back with the time they had left, also after a server restart (the field is
///         saved, and <see cref="TimeService.ActiveTime" /> doesn't run while the server is down).
///     </para>
///     <para>
///         Nobody to help means no window: when a player goes down while nobody else online is up, or the last player who
///         is up leaves, every downed player's window ends at once.
///     </para>
/// </remarks>
internal sealed class RescueService(
    IPacketSender packetSender,
    PlayerManager playerManager,
    TimeService timeService,
    PartnerInteractions partnerInteractions,
    EntityRegistry entityRegistry,
    WorldEntityManager worldEntityManager,
    EntitySimulation entitySimulation,
    IOptions<SubnauticaServerOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<RescueService> logger) : BackgroundService, ISessionCleaner
{
    public const string FIRST_AID_KIT_TECH_TYPE = "FirstAidKit";

    private static readonly TimeSpan tickInterval = TimeSpan.FromMilliseconds(250);

    private readonly IPacketSender packetSender = packetSender;
    private readonly PlayerManager playerManager = playerManager;
    private readonly TimeService timeService = timeService;
    private readonly PartnerInteractions partnerInteractions = partnerInteractions;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly IHostApplicationLifetime lifetime = lifetime;
    private readonly ILogger<RescueService> logger = logger;
    private readonly Lock gate = new();
    private double? lastTick;

    private double Now => timeService.ActiveTime.TotalSeconds;

    /// <summary>
    ///     The player's game says they went down. Grants the window or ends it at once.
    /// </summary>
    public async Task PlayerWentDownAsync(Player player)
    {
        float window;
        List<Player> expired;
        lock (gate)
        {
            if (player.DownedUntil.HasValue)
            {
                return;
            }
            double now = Now;
            if (!HasHelper(player, playerManager.GetConnectedPlayers()))
            {
                // Nobody can help: no window, and the count stays (it is about downs a partner had to deal with)
                window = 0;
                expired = TakeEveryoneDownIfNobodyIsUp(player);
            }
            else
            {
                int down = RescueRules.DownsBefore(player.DownsSinceRest, player.LastDownedAt, now) + 1;
                player.DownsSinceRest = down;
                player.LastDownedAt = now;
                window = RescueRules.WindowSeconds(options.Value.RescueWindowSeconds, options.Value.RescueEscalation, down);
                if (window > 0)
                {
                    player.DownedUntil = now + window;
                }
                expired = [];
            }
        }

        if (window > 0)
        {
            logger.ZLogInformation($"{player.Name} is down: {window} s to revive them (down {player.DownsSinceRest} since they rested)");
            await packetSender.SendPacketToAllAsync(new PlayerDowned(player.SessionId, window));
            return;
        }
        logger.ZLogInformation($"{player.Name} went down without a rescue window (down {player.DownsSinceRest} since they rested, {(expired.Count > 0 ? "nobody is up to help" : "escalation or option")})");
        await packetSender.SendPacketToAllAsync(new PlayerRescueExpired(player.SessionId));
        await ExpireAsync(expired.Where(p => p != player));
    }

    /// <summary>
    ///     A partner finished a revive hold.
    /// </summary>
    public async Task ReviveAsync(Player rescuer, PlayerReviveRequest packet)
    {
        PartnerInteractionResult result = partnerInteractions.Check(rescuer, packet.TargetId, PartnerInteractionRules.TargetMayBeDowned, out Player? target);
        if (result != PartnerInteractionResult.Allowed || target == null)
        {
            logger.ZLogDebug($"Refused a revive of session {packet.TargetId} by {rescuer.Name}: {result}");
            return;
        }

        NitroxId? kitId = null;
        lock (gate)
        {
            if (!target.DownedUntil.HasValue)
            {
                logger.ZLogDebug($"Refused a revive of {target.Name} by {rescuer.Name}: not down");
                return;
            }
            if (packet.Method == PlayerReviveRequest.ReviveMethod.FIRST_AID_KIT)
            {
                if (!IsKitOf(rescuer, packet.KitId, out string? refusal))
                {
                    logger.ZLogDebug($"Refused a revive of {target.Name} by {rescuer.Name} with a first aid kit: {refusal}");
                    return;
                }
                kitId = packet.KitId.Value;
                entitySimulation.EntityDestroyed(kitId);
                worldEntityManager.TryDestroyEntity(kitId, out _);
            }
            target.DownedUntil = null;
        }

        float health = RescueRules.HealthFor(packet.Method);
        logger.ZLogInformation($"{rescuer.Name} revived {target.Name} ({packet.Method}, {health} health)");
        if (kitId != null)
        {
            // To everyone, the rescuer too: their game removes the kit from the inventory when it gets this
            await packetSender.SendPacketToAllAsync(new EntityDestroyed(kitId));
        }
        await packetSender.SendPacketToAllAsync(new PlayerRevived(target.SessionId, rescuer.SessionId, health));
    }

    /// <summary>
    ///     The player died (their game's death event, or it says they died while down): a window still open ends without
    ///     a death of its own, and the others stop showing them as down.
    /// </summary>
    public async Task PlayerDiedAsync(Player player)
    {
        lock (gate)
        {
            if (!player.DownedUntil.HasValue)
            {
                return;
            }
            player.DownedUntil = null;
        }
        logger.ZLogInformation($"{player.Name} died while down");
        await packetSender.SendPacketToOthersAsync(new PlayerRescueExpired(player.SessionId), player.SessionId);
    }

    /// <summary>
    ///     A player finished joining: they get the downed partners' windows, and if they quit while down, they are down again
    ///     with the time they had left.
    /// </summary>
    public async Task PlayerJoinedAsync(Player player)
    {
        List<(Player Player, float Remaining)> downed = [];
        List<Player> expired = [];
        lock (gate)
        {
            double now = Now;
            foreach (Player other in playerManager.GetConnectedPlayers())
            {
                if (other.DownedUntil.HasValue && other != player)
                {
                    downed.Add((other, Remaining(other, now)));
                }
            }
            if (player.DownedUntil.HasValue)
            {
                downed.Add((player, Remaining(player, now)));
                expired = TakeEveryoneDownIfNobodyIsUp(null);
            }
        }

        foreach ((Player other, float remaining) in downed)
        {
            if (other == player)
            {
                logger.ZLogInformation($"{player.Name} rejoined while down: {remaining} s left");
                await packetSender.SendPacketToAllAsync(new PlayerDowned(player.SessionId, remaining));
            }
            else
            {
                await packetSender.SendPacketAsync(new PlayerDowned(other.SessionId, remaining), player.SessionId);
            }
        }
        await ExpireAsync(expired);
    }

    /// <summary>One line per player who went down since they rested, for the <c>rescue status</c> command.</summary>
    public List<string> Describe()
    {
        SubnauticaServerOptions current = options.Value;
        List<string> lines = [$"Rescue window: {(current.RescueWindowSeconds > 0 ? $"{current.RescueWindowSeconds} s" : "off")}, escalation {(current.RescueEscalation ? "on" : "off")}"];
        lock (gate)
        {
            double now = Now;
            foreach (Player player in playerManager.GetAllPlayers())
            {
                int downs = RescueRules.DownsBefore(player.DownsSinceRest, player.LastDownedAt, now);
                if (!player.DownedUntil.HasValue && downs == 0)
                {
                    continue;
                }
                string state = player.DownedUntil.HasValue ? $"down, {Remaining(player, now):0} s left{(player.IsOnline ? "" : " (offline, paused)")}" : "up";
                float next = RescueRules.WindowSeconds(current.RescueWindowSeconds, current.RescueEscalation, downs + 1);
                lines.Add($"{player.Name}: {state}; {downs} down(s) since they rested, next window {next} s");
            }
        }
        return lines;
    }

    public async Task OnEventAsync(ISessionCleaner.Args args)
    {
        // The leaver's own window pauses (Tick). If nobody who is up stays, the others' windows end, but not while the
        // server shuts down: then everyone leaves, in any order, and a downed player must come back down after the restart.
        if (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            return;
        }
        List<Player> expired = [];
        lock (gate)
        {
            List<Player> remaining = playerManager.GetConnectedPlayersExcept(args.Session.Id);
            if (remaining.Count > 0 && remaining.All(p => p.DownedUntil.HasValue))
            {
                foreach (Player player in remaining)
                {
                    player.DownedUntil = null;
                    expired.Add(player);
                }
            }
        }
        if (expired.Count > 0)
        {
            logger.ZLogInformation($"Nobody who is up is left online: the rescue window of {string.Join(", ", expired.Select(p => p.Name))} ends");
        }
        await ExpireAsync(expired, args.Session.Id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(tickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"Rescue window tick failed");
            }
        }
    }

    /// <summary>
    ///     Ends the windows that ran out and pauses the windows of downed players who are offline.
    /// </summary>
    internal async Task TickAsync()
    {
        List<Player> expired = [];
        lock (gate)
        {
            double now = Now;
            double elapsed = lastTick.HasValue ? Math.Max(0, now - lastTick.Value) : 0;
            lastTick = now;
            foreach (Player player in playerManager.GetAllPlayers())
            {
                if (!player.DownedUntil.HasValue)
                {
                    continue;
                }
                if (!player.IsOnline)
                {
                    player.DownedUntil += elapsed;
                }
                else if (player.DownedUntil.Value <= now)
                {
                    player.DownedUntil = null;
                    expired.Add(player);
                }
            }
        }
        foreach (Player player in expired)
        {
            logger.ZLogInformation($"The rescue window of {player.Name} ran out");
        }
        await ExpireAsync(expired);
    }

    private static float Remaining(Player player, double now) => (float)Math.Max(0, player.DownedUntil!.Value - now);

    /// <summary>
    ///     Someone online other than <paramref name="player" /> who isn't down.
    /// </summary>
    private static bool HasHelper(Player player, List<Player> connected) => connected.Any(other => other != player && !other.DownedUntil.HasValue);

    /// <summary>
    ///     When nobody online is up (counting <paramref name="goingDown" /> as down), clears every online downed player's
    ///     window and returns them. Call under <see cref="gate" />.
    /// </summary>
    private List<Player> TakeEveryoneDownIfNobodyIsUp(Player? goingDown)
    {
        List<Player> connected = playerManager.GetConnectedPlayers();
        if (connected.Any(p => p != goingDown && !p.DownedUntil.HasValue))
        {
            return [];
        }
        List<Player> expired = [];
        foreach (Player player in connected)
        {
            player.DownedUntil = null;
            expired.Add(player);
        }
        return expired;
    }

    private bool IsKitOf(Player rescuer, Optional<NitroxId> kitId, out string? refusal)
    {
        if (!kitId.HasValue || !entityRegistry.TryGetEntityById(kitId.Value, out Entity? kit))
        {
            refusal = "unknown kit";
            return false;
        }
        if (kit is not InventoryItemEntity || kit.TechType?.Name != FIRST_AID_KIT_TECH_TYPE)
        {
            refusal = $"not a first aid kit ({kit.GetType().Name}, {kit.TechType})";
            return false;
        }
        if (kit.ParentId != rescuer.GameObjectId)
        {
            refusal = $"not in the rescuer's inventory (parent {kit.ParentId})";
            return false;
        }
        refusal = null;
        return true;
    }

    private async Task ExpireAsync(IEnumerable<Player> players, SessionId? leaving = null)
    {
        foreach (Player player in players)
        {
            PlayerRescueExpired packet = new(player.SessionId);
            if (leaving.HasValue)
            {
                await packetSender.SendPacketToOthersAsync(packet, leaving.Value);
            }
            else
            {
                await packetSender.SendPacketToAllAsync(packet);
            }
        }
    }
}
