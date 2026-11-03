using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): who holds on to which Seamoth. The hitcher's game asks with
///     <see cref="PlayerHitched" />; the server grants it if a partner pilots that Seamoth and the hitcher is close to it,
///     and tells everyone. A hitch ends when the hitcher lets go (<see cref="PlayerUnhitched" /> from their game), or here
///     when the pilot gets out or leaves the game, the Seamoth docks or is destroyed, or the hitcher leaves. Players who
///     join later get the hitches that are on. Kept in memory only: a hitch doesn't survive a rejoin or a restart.
/// </summary>
internal sealed class HitchService(IPacketSender packetSender, PlayerManager playerManager, EntityRegistry entityRegistry, IOptions<SubnauticaServerOptions> options, ILogger<HitchService> logger)
    : ISessionCleaner
{
    /// <summary>
    ///     How far the hitcher may be from the Seamoth's origin, by the server's positions. The game lets a player target the
    ///     hull from 2 m (camera to collider); the Seamoth is about 5 m long, and both positions lag behind.
    /// </summary>
    public const float MAX_DISTANCE = 8f;

    public const string SEAMOTH_TECH_TYPE = "Seamoth";

    private readonly IPacketSender packetSender = packetSender;
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<HitchService> logger = logger;
    private readonly Lock gate = new();
    private readonly Dictionary<SessionId, Hitch> hitches = [];

    public enum HitchResult
    {
        Allowed,
        Disabled,
        SenderDowned,
        SenderPiloting,
        NotASeamoth,
        NoPilot,
        AlreadyTaken,
        TooFar
    }

    /// <summary>
    ///     The vehicle the player holds on to, if any.
    /// </summary>
    public bool TryGetHitch(SessionId playerId, out NitroxId vehicleId)
    {
        lock (gate)
        {
            if (hitches.TryGetValue(playerId, out Hitch hitch))
            {
                vehicleId = hitch.VehicleId;
                return true;
            }
        }
        vehicleId = null!;
        return false;
    }

    /// <summary>
    ///     The sender's game asks to hold on to <paramref name="vehicleId" />. Granted: everyone gets
    ///     <see cref="PlayerHitched" />, the sender included.
    /// </summary>
    public async Task<HitchResult> HitchAsync(Player sender, NitroxId vehicleId)
    {
        HitchResult result;
        Player? pilot = null;
        lock (gate)
        {
            result = Check(sender, vehicleId, out pilot);
            if (result == HitchResult.Allowed)
            {
                hitches[sender.SessionId] = new Hitch(vehicleId, pilot!.SessionId);
            }
        }
        if (result != HitchResult.Allowed)
        {
            logger.ZLogDebug($"{sender.Name} can't hold on to {vehicleId}: {result}");
            return result;
        }
        logger.ZLogInformation($"{sender.Name} holds on to {pilot!.Name}'s Seamoth {vehicleId}");
        await packetSender.SendPacketToAllAsync(new PlayerHitched(sender.SessionId, vehicleId));
        return result;
    }

    /// <summary>
    ///     The hitcher's game let go at <paramref name="position" />: the others place the body there.
    /// </summary>
    public async Task UnhitchedByPlayerAsync(Player sender, NitroxVector3 position)
    {
        lock (gate)
        {
            if (!hitches.Remove(sender.SessionId))
            {
                return;
            }
        }
        sender.Position = position;
        logger.ZLogInformation($"{sender.Name} let go of the Seamoth");
        await packetSender.SendPacketToOthersAsync(new PlayerUnhitched(sender.SessionId, position), sender.SessionId);
    }

    /// <summary>
    ///     Everyone holding on to <paramref name="vehicleId" /> lets go: the pilot got out, it docked or was destroyed.
    /// </summary>
    public Task ReleaseVehicleAsync(NitroxId vehicleId, string reason)
    {
        List<SessionId> released;
        lock (gate)
        {
            released = hitches.Where(pair => pair.Value.VehicleId == vehicleId).Select(pair => pair.Key).ToList();
            foreach (SessionId hitcher in released)
            {
                hitches.Remove(hitcher);
            }
        }
        return SendReleasedAsync(released, reason, null);
    }

    /// <summary>
    ///     A player started or stopped piloting: when the pilot gets out, the hitchers let go; a hitcher who gets into a
    ///     vehicle (their game should have let go already) isn't holding on anymore.
    /// </summary>
    public async Task PilotChangedAsync(Player player, NitroxId vehicleId, bool isPiloting)
    {
        if (isPiloting)
        {
            List<SessionId> released = [];
            lock (gate)
            {
                if (hitches.Remove(player.SessionId))
                {
                    released.Add(player.SessionId);
                }
            }
            await SendReleasedAsync(released, "they got into a vehicle", null);
            return;
        }
        await ReleaseVehicleAsync(vehicleId, $"{player.Name} got out");
    }

    /// <summary>
    ///     A player finished joining: they see who holds on to which Seamoth.
    /// </summary>
    public async Task PlayerJoinedAsync(Player player)
    {
        List<KeyValuePair<SessionId, Hitch>> current;
        lock (gate)
        {
            current = hitches.Where(pair => pair.Key != player.SessionId).ToList();
        }
        foreach (KeyValuePair<SessionId, Hitch> hitch in current)
        {
            await packetSender.SendPacketAsync(new PlayerHitched(hitch.Key, hitch.Value.VehicleId), player.SessionId);
        }
    }

    public Task OnEventAsync(ISessionCleaner.Args args)
    {
        SessionId leaving = args.Session.Id;
        List<SessionId> released;
        bool leaverWasHitched;
        lock (gate)
        {
            leaverWasHitched = hitches.Remove(leaving);
            released = hitches.Where(pair => pair.Value.PilotId == leaving).Select(pair => pair.Key).ToList();
            foreach (SessionId hitcher in released)
            {
                hitches.Remove(hitcher);
            }
        }
        if (leaverWasHitched)
        {
            logger.ZLogInformation($"Session {leaving} left while holding on to a Seamoth");
        }
        // The leaver's body is removed from the others' games anyway; only the hitchers of a pilot who left let go
        return SendReleasedAsync(released, "the pilot left the game", leaving);
    }

    private HitchResult Check(Player sender, NitroxId vehicleId, out Player? pilot)
    {
        pilot = null;
        if (!options.Value.SeamothHitch)
        {
            return HitchResult.Disabled;
        }
        if (sender.DownedUntil.HasValue)
        {
            return HitchResult.SenderDowned;
        }
        if (sender.PlayerContext?.DrivingVehicle != null)
        {
            return HitchResult.SenderPiloting;
        }
        if (!entityRegistry.TryGetEntityById(vehicleId, out VehicleEntity? vehicle) || vehicle.TechType.Name != SEAMOTH_TECH_TYPE)
        {
            return HitchResult.NotASeamoth;
        }
        pilot = playerManager.GetConnectedPlayersExcept(sender).FirstOrDefault(other => other.PlayerContext?.DrivingVehicle == vehicleId);
        if (pilot == null)
        {
            return HitchResult.NoPilot;
        }
        if (hitches.Values.Any(hitch => hitch.VehicleId == vehicleId))
        {
            return HitchResult.AlreadyTaken;
        }
        if (!(NitroxVector3.Distance(sender.Position, vehicle.Transform.Position) <= MAX_DISTANCE))
        {
            return HitchResult.TooFar;
        }
        return HitchResult.Allowed;
    }

    private async Task SendReleasedAsync(List<SessionId> released, string reason, SessionId? exclude)
    {
        foreach (SessionId hitcher in released)
        {
            NitroxVector3 position = playerManager.TryGetPlayerBySessionId(hitcher, out Player? player) ? player.Position : NitroxVector3.Zero;
            logger.ZLogInformation($"{player?.Name ?? hitcher.ToString()} lets go of the Seamoth: {reason}");
            PlayerUnhitched packet = new(hitcher, position);
            if (exclude.HasValue)
            {
                await packetSender.SendPacketToOthersAsync(packet, exclude.Value);
            }
            else
            {
                await packetSender.SendPacketToAllAsync(packet);
            }
        }
    }

    private readonly record struct Hitch(NitroxId VehicleId, SessionId PilotId);
}
