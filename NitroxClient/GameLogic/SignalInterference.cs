using System.Collections.Generic;
using Nitrox.Model.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.GameLogic;

/// <summary>
///     Who is in signal interference: caves and the deep cut the line between the players (COOP_FEATURES_SPEC D2).
///     Read by the pings (F2); the partner marker (F1/D2) can read it too. Both parts are off when the server option
///     <c>PartnerSignalInterference</c> is off.
/// </summary>
/// <remarks>
///     The local player's state is the hook D2 fills in: <see cref="IsLocalPlayerInterfered" />. The partners' states come
///     from their games, as <c>PlayerStats.Interfered</c> (every 3 s, see <see cref="PlayerStatsBroadcaster" />).
/// </remarks>
public sealed class SignalInterference
{
    private readonly LocalPlayer localPlayer;
    private readonly Dictionary<SessionId, bool> partners = [];

    public SignalInterference(LocalPlayer localPlayer, PlayerManager playerManager)
    {
        this.localPlayer = localPlayer;
        playerManager.OnRemove += (sessionId, _) => partners.Remove(sessionId);
    }

#if DEBUG
    /// <summary>
    ///     Dev bridge and scenarios only: puts the local player in interference. Still off when the server option is off.
    /// </summary>
    public static bool ForceLocalInterfered;
#endif

    private bool Enabled => localPlayer.CoopSettings.PartnerSignalInterference;

    /// <summary>
    ///     <b>D2 hook.</b> Whether the local player is in interference right now. D2 computes it here: deeper than
    ///     <c>CoopSettings.SignalInterferenceDepth</c> or in an interference biome, and no beacon or scanner room
    ///     relaying (COOP_FEATURES_SPEC D2 "Rule"). Until D2 is built the local player is never in interference.
    ///     This is also what this game sends to the others as <c>PlayerStats.Interfered</c>.
    /// </summary>
    public bool IsLocalPlayerInterfered()
    {
        if (!Enabled)
        {
            return false;
        }
#if DEBUG
        if (ForceLocalInterfered)
        {
            return true;
        }
#endif
        return false;
    }

    /// <summary>
    ///     The partner's state as their game last reported it (false until their first <c>PlayerStats</c>).
    /// </summary>
    public bool IsPartnerInterfered(SessionId sessionId) => Enabled && partners.TryGetValue(sessionId, out bool interfered) && interfered;

    public void SetPartnerInterfered(SessionId sessionId, bool interfered) => partners[sessionId] = interfered;
}
