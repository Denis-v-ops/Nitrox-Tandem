using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Nitrox.Model.Constants;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Model.MultiplayerSession;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Server.Subnautica.Models.AppEvents;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Services;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

// TODO: This manager should only handle player data. Move connection related state to other managers.
internal sealed partial class PlayerManager(SessionManager sessionManager, BanService banService, IOptions<SubnauticaServerOptions> options, ILogger<PlayerManager> logger) : ISessionCleaner
{
    [GeneratedRegex(NitroxConstants.PLAYER_NAME_VALID_REGEX, RegexOptions.NonBacktracking)]
    private static partial Regex PlayerNameRegex();

    private readonly ThreadSafeDictionary<string, Player> allPlayersByName = [];
    private readonly ThreadSafeDictionary<SessionId, Player> connectedPlayersBySessionId = [];
    private readonly ThreadSafeDictionary<SessionId, PlayerContext> reservations = [];
    private readonly ThreadSafeSet<string> reservedPlayerNames = new("Player"); // "Player" is often used to identify the local player and should not be used by any user

    private readonly SessionManager sessionManager = sessionManager;
    private readonly BanService banService = banService;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<PlayerManager> logger = logger;
    private PeerId currentPlayerId;

    /// <summary>All players that have joined since the server started, even if they disconnected</summary>
    public IEnumerable<Player> GetAllPlayers() => allPlayersByName.Values;

    public IEnumerable<Player> ConnectedPlayers()
    {
        return connectedPlayersBySessionId.Values;
    }

    public List<Player> GetConnectedPlayers() => ConnectedPlayers().ToList();

    public List<Player> GetConnectedPlayersExcept(Player excludePlayer)
    {
        return ConnectedPlayers().Where(player => player != excludePlayer).ToList();
    }

    public List<Player> GetConnectedPlayersExcept(SessionId excludeSessionId)
    {
        return ConnectedPlayers().Where(player => player.SessionId != excludeSessionId).ToList();
    }

    public PlayerContext? GetPlayerReservation(SessionId sessionId)
    {
        return reservations.TryGetValue(sessionId, out PlayerContext player) ? player : null;
    }

    public void AddSavedPlayer(Player player)
    {
        allPlayersByName.Add(player.Name, player);
        currentPlayerId = allPlayersByName.Values.Max(x => x.PeerId);
    }

    public MultiplayerSessionReservation ReservePlayerContext(
        SessionId sessionId,
        IPEndPoint endPoint,
        PlayerSettings playerSettings,
        AuthenticationContext authenticationContext)
    {
        if (banService.IsBanned(endPoint.Address))
        {
            MultiplayerSessionReservationState rejectedState = MultiplayerSessionReservationState.REJECTED | MultiplayerSessionReservationState.PLAYER_BANNED;
            return new MultiplayerSessionReservation(sessionId, rejectedState, banService.GetBanRejectionDetail(endPoint.Address));
        }

        if (sessionManager.GetSessionCount() > options.Value.MaxConnections)
        {
            MultiplayerSessionReservationState rejectedState = MultiplayerSessionReservationState.REJECTED | MultiplayerSessionReservationState.SERVER_PLAYER_CAPACITY_REACHED;
            return new MultiplayerSessionReservation(sessionId, rejectedState);
        }

        if (!string.IsNullOrEmpty(options.Value.ServerPassword) && (!authenticationContext.ServerPassword.HasValue || authenticationContext.ServerPassword.Value != options.Value.ServerPassword))
        {
            MultiplayerSessionReservationState rejectedState = MultiplayerSessionReservationState.REJECTED | MultiplayerSessionReservationState.AUTHENTICATION_FAILED;
            return new MultiplayerSessionReservation(sessionId, rejectedState);
        }


        if (!PlayerNameRegex().IsMatch(authenticationContext.Username))
        {
            MultiplayerSessionReservationState rejectedState = MultiplayerSessionReservationState.REJECTED | MultiplayerSessionReservationState.INCORRECT_USERNAME;
            return new MultiplayerSessionReservation(sessionId, rejectedState);
        }

        string playerName = authenticationContext.Username;

        allPlayersByName.TryGetValue(playerName, out Player? player);
        if (player?.IsPermaDeath == true && options.Value.IsHardcore())
        {
            MultiplayerSessionReservationState rejectedState = MultiplayerSessionReservationState.REJECTED | MultiplayerSessionReservationState.HARDCORE_PLAYER_DEAD;
            return new MultiplayerSessionReservation(sessionId, rejectedState);
        }

        if (reservedPlayerNames.Contains(playerName))
        {
            MultiplayerSessionReservationState rejectedState = MultiplayerSessionReservationState.REJECTED | MultiplayerSessionReservationState.UNIQUE_PLAYER_NAME_CONSTRAINT_VIOLATED;
            return new MultiplayerSessionReservation(sessionId, rejectedState);
        }
        reservedPlayerNames.Add(playerName);

        bool hasSeenPlayerBefore = player != null;
        NitroxId playerNitroxId = hasSeenPlayerBefore ? player.GameObjectId : new NitroxId();
        SubnauticaGameMode gameMode = hasSeenPlayerBefore ? player.GameMode : options.Value.GameMode;
        IntroCinematicMode introCinematicMode = hasSeenPlayerBefore ? IntroCinematicMode.COMPLETED : IntroCinematicMode.LOADING;
        PlayerAnimation animation = new(AnimChangeType.UNDERWATER, AnimChangeState.ON);

        SessionManager.Session session = sessionManager.GetOrCreateSession(endPoint);

        // TODO: At some point, store the muted state of a player
        PlayerContext playerContext = new(playerName, session.Id, playerNitroxId, !hasSeenPlayerBefore, playerSettings, false, gameMode, null, introCinematicMode, animation);

        reservations.Add(session.Id, playerContext);

        return new MultiplayerSessionReservation(sessionId);
    }

    /// <summary>
    ///     Whether a session holds this name: a connected player or a reservation.
    /// </summary>
    public bool IsNameReserved(string playerName) => reservedPlayerNames.Contains(playerName);

    /// <summary>
    ///     Another session that holds <paramref name="playerName" /> (joined or only reserved) and comes from the same IP
    ///     address as the requesting one. That is the same player, whose game crashed or lost the connection: the server
    ///     keeps the old session until it times out, and until then the name is "in use".
    /// </summary>
    public bool TryFindStaleSession(string playerName, SessionId requestingSessionId, IPAddress requestingAddress, out SessionId staleSessionId)
    {
        staleSessionId = default;
        SessionId? holder = ConnectedPlayers().FirstOrDefault(player => player.Name == playerName && player.SessionId != requestingSessionId)?.SessionId ??
                            reservations.Values.FirstOrDefault(reservation => reservation.PlayerName == playerName && reservation.SessionId != requestingSessionId)?.SessionId;
        if (holder is not { } holderId || sessionManager.GetEndPoint(holderId) is not { } holderEndPoint)
        {
            return false;
        }
        if (!Normalize(holderEndPoint.Address).Equals(Normalize(requestingAddress)))
        {
            return false;
        }
        staleSessionId = holderId;
        return true;

        // The server socket is dual-mode (IPv6Enabled): the same IPv4 client can show up as ::ffff:a.b.c.d
        static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    public Player CreatePlayerData(SessionId sessionId, out bool wasBrandNewPlayer)
    {
        PlayerContext playerContext = reservations[sessionId];
        Validate.NotNull(playerContext);

        bool isKnown = allPlayersByName.TryGetValue(playerContext.PlayerName, out Player player);
        if (playerContext.WasBrandNewPlayer && isKnown)
        {
            // Reserved before the saved players were loaded (the reservation processor waits for the world, this is the
            // fallback). As a new player the saved one would get a new body, the pod's starting items and the intro.
            logger.ZLogWarning($"{playerContext.PlayerName} was reserved as a new player, but a saved player of that name exists now: joining as the saved player");
            playerContext = new PlayerContext(playerContext.PlayerName, playerContext.SessionId, player.GameObjectId, false, playerContext.PlayerSettings, playerContext.IsMuted, player.GameMode,
                                              playerContext.DrivingVehicle, IntroCinematicMode.COMPLETED, playerContext.Animation);
        }
        wasBrandNewPlayer = playerContext.WasBrandNewPlayer;

        if (!isKnown)
        {
            player = new Player(++currentPlayerId,
                                sessionId,
                                playerContext.PlayerName,
                                false,
                                playerContext,
                                NitroxVector3.Zero,
                                NitroxQuaternion.Identity,
                                playerContext.PlayerNitroxId,
                                Optional.Empty,
                                options.Value.DefaultPlayerPerm,
                                new(options.Value.DefaultOxygenValue, options.Value.DefaultMaxOxygenValue, options.Value.DefaultHealthValue, options.Value.DefaultHungerValue, options.Value.DefaultThirstValue, options.Value.DefaultInfectionValue),
                                options.Value.GameMode,
                                [],
                                [],
                                new Dictionary<string, NitroxId>(),
                                new Dictionary<string, float>(),
                                new Dictionary<string, PingInstancePreference>(),
                                [],
                                false,
                                true
            );
            allPlayersByName[playerContext.PlayerName] = player;
        }
        else
        {
            player.SessionId = sessionId;
        }

        connectedPlayersBySessionId.Add(playerContext.SessionId, player);

        // TODO: make a ConnectedPlayer wrapper so this is not stateful
        player.PlayerContext = playerContext;
        // A joining game starts with empty hands and sends its pose again (COOP_FEATURES_SPEC R2a)
        player.Animations.Clear();
        player.HeldItem = null;

        // reconnecting players need to have their cell visibility refreshed
        player.ClearVisibleCells();

        reservations.Remove(sessionId);

        return player;
    }

    public bool SetPlayerProperty<T>(SessionId sessionId, T value, Action<Player, T> action)
    {
        if (!TryGetPlayerBySessionId(sessionId, out Player? player))
        {
            return false;
        }

        action(player, value);
        return true;
    }

    public bool TryGetPlayerByName(string playerName, [NotNullWhen(true)] out Player? foundPlayer)
    {
        foundPlayer = null;
        foreach (Player player in ConnectedPlayers())
        {
            if (player.Name == playerName)
            {
                foundPlayer = player;
                return true;
            }
        }

        return false;
    }

    public bool TryGetPlayerBySessionId(SessionId sessionId, [NotNullWhen(true)] out Player? player)
    {
        return connectedPlayersBySessionId.TryGetValue(sessionId, out player);
    }

    public Task OnEventAsync(ISessionCleaner.Args args)
    {
        reservations.Remove(args.Session.Id, out PlayerContext? reservation);
        if (!connectedPlayersBySessionId.Remove(args.Session.Id, out Player player))
        {
            // Left before joining (e.g. the game crashed while loading): free the name the reservation took
            if (reservation != null)
            {
                reservedPlayerNames.Remove(reservation.PlayerName);
            }
            return Task.CompletedTask;
        }
        reservedPlayerNames.Remove(player.Name);
        player.IsOnline = false;
        logger.ZLogInformation($"{player.Name} left the game");
        return Task.CompletedTask;
    }
}
