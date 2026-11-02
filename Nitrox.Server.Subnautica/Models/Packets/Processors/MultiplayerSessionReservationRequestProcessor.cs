using System.Diagnostics;
using Nitrox.Model.Core;
using Nitrox.Model.MultiplayerSession;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Server.Subnautica.Models.Administration;
using Nitrox.Server.Subnautica.Models.Communication;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Serialization.World;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

internal sealed class MultiplayerSessionReservationRequestProcessor(
    PlayerManager playerManager,
    SessionManager sessionManager,
    IKickPlayer playerKicker,
    Func<WorldService> worldServiceProvider,
    IHostApplicationLifetime lifetime,
    ILogger<MultiplayerSessionReservationRequestProcessor> logger)
    : IAnonPacketProcessor<MultiplayerSessionReservationRequest>
{
    /// <summary>
    ///     How long a reservation waits for the server to drop the stale session of the same player.
    /// </summary>
    private static readonly TimeSpan staleSessionRemovalTimeout = TimeSpan.FromSeconds(3);

    private readonly PlayerManager playerManager = playerManager;
    private readonly SessionManager sessionManager = sessionManager;
    private readonly IKickPlayer playerKicker = playerKicker;
    private readonly Func<WorldService> worldServiceProvider = worldServiceProvider;
    private readonly IHostApplicationLifetime lifetime = lifetime;
    private readonly ILogger<MultiplayerSessionReservationRequestProcessor> logger = logger;

    public async Task Process(AnonProcessorContext context, MultiplayerSessionReservationRequest packet)
    {
        logger.ZLogInformation($"Processing reservation request from {packet.AuthenticationContext.Username}");

        // The network accepts players while the world still loads (services start together). A reservation made before the
        // saved players are known treats a returning player as new. The client waits for the answer meanwhile.
        Task worldLoaded = worldServiceProvider().WaitForWorldLoadedAsync(lifetime.ApplicationStopping);
        if (!worldLoaded.IsCompleted)
        {
            logger.ZLogInformation($"The world is still loading, the reservation of {packet.AuthenticationContext.Username} waits for it");
            await worldLoaded;
        }

        PlayerSettings playerSettings = packet.PlayerSettings;
        AuthenticationContext authenticationContext = packet.AuthenticationContext;

        if (playerManager.TryFindStaleSession(authenticationContext.Username, context.Sender.SessionId, context.Sender.EndPoint.Address, out SessionId staleSessionId))
        {
            await RemoveStaleSessionAsync(authenticationContext.Username, staleSessionId);
        }

        MultiplayerSessionReservation reservation = playerManager.ReservePlayerContext(
            context.Sender.SessionId,
            context.Sender.EndPoint,
            playerSettings,
            authenticationContext);

        logger.ZLogInformation($"Reservation processed successfully: Username: {packet.AuthenticationContext.Username} - {reservation}");
        await context.ReplyAsync(reservation);
    }

    /// <summary>
    ///     The player comes back from the same address while the server still holds their old session, which it would only
    ///     drop when the connection times out (30 s, 5 min in Debug): kick it now so the name is free.
    /// </summary>
    private async Task RemoveStaleSessionAsync(string playerName, SessionId staleSessionId)
    {
        logger.ZLogInformation($"{playerName} connects again from the same address while session #{staleSessionId} still holds the name, removing the old session");
        await playerKicker.KickPlayer(staleSessionId, "Connected again from the same address");
        // The kick's disconnect removes the session too, on the network thread; whichever comes first runs the clean-up
        await sessionManager.RemoveSessionAsync(staleSessionId);

        Stopwatch waited = Stopwatch.StartNew();
        while (playerManager.IsNameReserved(playerName) && waited.Elapsed < staleSessionRemovalTimeout)
        {
            await Task.Delay(50);
        }
        if (playerManager.IsNameReserved(playerName))
        {
            logger.ZLogWarning($"Session #{staleSessionId} still holds {playerName} after {staleSessionRemovalTimeout.TotalSeconds} s");
        }
    }
}
