using System.Collections.Generic;
using Nitrox.Model.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     Who is in signal interference: caves and the deep cut the line between the players (COOP_FEATURES_SPEC D2).
///     Read by the pings (F2) and the partner's marker (<see cref="PartnerSignal" />). Both parts are off when the server
///     option <c>PartnerSignalInterference</c> is off.
/// </summary>
/// <remarks>
///     Each game decides for its own player (<see cref="IsLocalPlayerInterfered" />, the rule is
///     <see cref="SignalInterferenceRules" />). The partners' states come from their games, as <c>PlayerStats.Interfered</c>
///     (every 3 s, see <see cref="PlayerStatsBroadcaster" />).
/// </remarks>
public sealed class SignalInterference
{
    /// <summary>
    ///     The local state is worked out at most this often; the marker asks four times a second per partner.
    /// </summary>
    private const float LOCAL_INTERVAL = 0.25f;

    private readonly LocalPlayer localPlayer;
    private readonly Dictionary<SessionId, bool> partners = [];
    private float localComputedAt = float.MinValue;
    private bool localInterfered;

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

    /// <summary>
    ///     Why the local player is or isn't in interference, as last worked out (for the dev bridge).
    /// </summary>
    public static string LocalReason { get; private set; } = "";

    private bool Enabled => localPlayer.CoopSettings.PartnerSignalInterference;

    /// <summary>
    ///     Whether the local player is in interference right now: deeper than <c>CoopSettings.SignalInterferenceDepth</c> or
    ///     in an interference biome, and no deployed beacon or powered scanner room relaying (COOP_FEATURES_SPEC D2 "Rule").
    ///     This is also what this game sends to the others as <c>PlayerStats.Interfered</c>.
    /// </summary>
    public bool IsLocalPlayerInterfered()
    {
        if (!Enabled)
        {
            LocalReason = "off (server option)";
            return false;
        }
#if DEBUG
        if (ForceLocalInterfered)
        {
            LocalReason = "forced";
            return true;
        }
#endif
        float now = Time.unscaledTime;
        if (now - localComputedAt >= LOCAL_INTERVAL || now < localComputedAt)
        {
            localComputedAt = now;
            localInterfered = ComputeLocal();
        }
        return localInterfered;
    }

    /// <summary>
    ///     The partner's state as their game last reported it (false until their first <c>PlayerStats</c>).
    /// </summary>
    public bool IsPartnerInterfered(SessionId sessionId) => Enabled && partners.TryGetValue(sessionId, out bool interfered) && interfered;

    public void SetPartnerInterfered(SessionId sessionId, bool interfered) => partners[sessionId] = interfered;

    /// <summary>
    ///     Whether the line to this partner is cut: either of the two is in interference.
    /// </summary>
    public bool IsLinkCut(SessionId partner) => SignalInterferenceRules.IsLinkCut(IsLocalPlayerInterfered(), IsPartnerInterfered(partner));

    private bool ComputeLocal()
    {
        Player player = Player.main;
        if (!player)
        {
            LocalReason = "no player";
            return false;
        }
        Vector3 position = player.transform.position;
        float depth = Ocean.GetDepthOf(position);
        string biome = player.GetBiomeString();
        int interferenceDepth = localPlayer.CoopSettings.SignalInterferenceDepth;
        bool tooDeep = SignalInterferenceRules.IsTooDeep(depth, interferenceDepth);
        if (!tooDeep && !SignalInterferenceRules.IsInterferenceBiome(biome))
        {
            LocalReason = $"clear (depth {depth:0} m, biome {biome})";
            return false;
        }
        string cause = tooDeep ? $"depth {depth:0} m > {interferenceDepth} m" : $"biome {biome}";
        string relay = FindRelay(position);
        LocalReason = relay == null ? $"interfered: {cause}" : $"relayed by {relay} ({cause})";
        return SignalInterferenceRules.IsInterfered(depth, biome, interferenceDepth, relay != null);
    }

    /// <summary>
    ///     A deployed beacon within 150 m or a powered scanner room within its scan range, or null.
    /// </summary>
    private static string FindRelay(Vector3 position)
    {
        // BeaconManager also lists beacons in inventories and lockers (Beacon.Awake adds every one); a deployed beacon was
        // dropped or thrown, so it isn't attached
        using (HashSet<Beacon>.Enumerator beacons = BeaconManager.GetEnumerator())
        {
            while (beacons.MoveNext())
            {
                Beacon beacon = beacons.Current;
                if (!beacon || !beacon.gameObject.activeInHierarchy || !beacon.pickupable || beacon.pickupable.attached)
                {
                    continue;
                }
                float distance = Vector3.Distance(position, beacon.transform.position);
                if (SignalInterferenceRules.IsRelayedByBeacon(distance))
                {
                    return $"beacon {distance:0} m";
                }
            }
        }

        // Every scanner room this game has loaded (MapRoomFunctionality.Start adds it). Powered as the room itself decides
        // it (game:MapRoomFunctionality.cs:177,236)
        foreach (MapRoomFunctionality mapRoom in MapRoomFunctionality.mapRooms)
        {
            if (!mapRoom)
            {
                continue;
            }
            bool powered = !GameModeUtils.RequiresPower() || mapRoom.powered;
            float distance = Vector3.Distance(position, mapRoom.transform.position);
            if (SignalInterferenceRules.IsRelayedByScannerRoom(distance, powered, mapRoom.GetScanRange()))
            {
                return $"scanner room {distance:0} m";
            }
        }
        return null;
    }
}
