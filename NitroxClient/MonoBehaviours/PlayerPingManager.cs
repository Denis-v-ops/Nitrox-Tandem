using System;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Gui.InGame;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Context pings (COOP_FEATURES_SPEC F2): the ping key picks Danger, Look or Come here from what the crosshair hits,
///     the "come here" key always sends Come here. Each game builds the label in its own language.
/// </summary>
internal sealed class PlayerPingManager : MonoBehaviour
{
    internal const float MAX_PING_DISTANCE = 250f;
    private const float MIN_PING_COOLDOWN = 0.5f;
    private const int MAX_ACTIVE_PINGS = 3;
    public const string PING_OK_SOUND = "event:/sub/cyclops/sonar";
    public const string PING_FAIL_SOUND = "event:/tools/divereel/breadcrum";

    private static PlayerPingManager instance = null!;

    private RaycastHit? queuedRaycastHit;
    private bool queuedComeHere;
    private IPacketSender packetSender = null!;
    private LocalPlayer localPlayer = null!;
    private PlayerManager playerManager = null!;
    private SignalInterference signalInterference = null!;
    private float lastPingTime;
    private readonly Dictionary<SessionId, List<PlayerPing>> playerPings = [];
    private readonly List<PlayerPing> localPlayerPings = new(MAX_ACTIVE_PINGS);

    /// <summary>Pings from others that weren't shown because of interference (the price, COOP_FEATURES_SPEC §2.2).</summary>
    public static int DroppedByInterference { get; private set; }

    public void Awake()
    {
        if (instance)
        {
            Log.Error($"Tried to initialize a second instance of singleton {nameof(PlayerPingManager)}:{Environment.NewLine}{Environment.StackTrace}");
            Destroy(this);
            return;
        }
        instance = this;
        packetSender = this.Resolve<IPacketSender>();
        localPlayer = this.Resolve<LocalPlayer>();
        playerManager = this.Resolve<PlayerManager>();
        signalInterference = this.Resolve<SignalInterference>();
    }

    private void Update()
    {
        if (queuedRaycastHit is { } hit && CanCreatePing())
        {
            queuedRaycastHit = null;
            CreatePing(hit.point + hit.normal * 0.5f, hit.collider.gameObject, queuedComeHere);
        }
    }

    /// <summary>The ping key: the kind comes from what the crosshair hits.</summary>
    public static void RequestPing()
    {
        if (!instance)
        {
            return;
        }
        instance.RequestPingInternal(false);
    }

    /// <summary>The "Ping: come here" key: always a "come here" ping where the crosshair hits.</summary>
    public static void RequestComeHerePing()
    {
        if (!instance)
        {
            return;
        }
        instance.RequestPingInternal(true);
    }

    private void RequestPingInternal(bool comeHere)
    {
        QueuePing(PingAim.FromCrosshair(MAX_PING_DISTANCE), comeHere);
    }

#if DEBUG
    /// <summary>
    ///     Dev bridge (<c>call PlayerPingManager.RequestPingToward x,y,z</c>): the ping key with the ray from the camera
    ///     toward a point instead of through the crosshair. The bridge can't turn the camera of a player in a seat.
    /// </summary>
    public static void RequestPingToward(Vector3 point)
    {
        if (!instance || !PingAim.TryGetRayToward(point, out Ray ray))
        {
            return;
        }
        instance.QueuePing(PingAim.Along(ray, MAX_PING_DISTANCE), false);
    }
#endif

    private void QueuePing(RaycastHit? hit, bool comeHere)
    {
        queuedRaycastHit = hit;
        queuedComeHere = comeHere;
        if (queuedRaycastHit == null)
        {
            // Invalid ping raycast so we play an error sound.
            FMODEmitterController.PlayEventOneShot(PING_FAIL_SOUND, 3, Player.main.transform.position);
        }
    }

    private bool CanCreatePing()
    {
        if (Time.realtimeSinceStartup - lastPingTime < MIN_PING_COOLDOWN)
        {
            return false;
        }
        if (!Player.main || !AvatarInputHandler.main.IsEnabled())
        {
            return false;
        }

        return true;
    }

    private void CreatePing(Vector3 position, GameObject entityHit, bool comeHere)
    {
        if (!entityHit)
        {
            return;
        }
        lastPingTime = Time.realtimeSinceStartup;
        if (!localPlayer.SessionId.HasValue)
        {
            return;
        }

        PingKind kind = PingKind.ComeHere;
        TechType targetTechType = TechType.None;
        if (!comeHere)
        {
            kind = PingTarget.Classify(entityHit, out targetTechType);
        }

        RemoveOldestIfFull(localPlayerPings);

        NitroxId pingId = new();
        SessionId sessionId = localPlayer.SessionId.Value;
        Color playerColor = localPlayer.PlayerSettings.PlayerColor.ToUnity();
        string label = BuildLabel(kind, localPlayer.PlayerName, targetTechType, FindPingedPlayerName(kind, targetTechType, position, sessionId));

        PlayerPing newPing = PlayerPing.SpawnPlayerPing(kind, position, localPlayer.PlayerName, label, targetTechType, pingId, playerColor, IsAnyPartnerAt);
        if (newPing)
        {
            localPlayerPings.Add(newPing);
        }

        packetSender.Send(new PlayerPingCreated(sessionId, kind, targetTechType.ToDto(), position.ToDto(), pingId));
    }

    public static void CreateRemotePing(PlayerPingCreated packet)
    {
        if (!instance)
        {
            return;
        }

        instance.CreateRemotePingInternal(packet);
    }

    private void CreateRemotePingInternal(PlayerPingCreated packet)
    {
        Vector3 position = packet.Position.ToUnity();
        Optional<RemotePlayer> sender = playerManager.Find(packet.SessionId);
        GameObject? senderBody = sender.HasValue ? sender.Value.Body : null;

        // The price (COOP_FEATURES_SPEC §2.2): when either player is in interference, only show it near the sender.
        // Without the sender's body, measure to the ping.
        Vector3 senderPosition = senderBody ? senderBody.transform.position : position;
        float distanceToSender = Player.main ? Vector3.Distance(Player.main.transform.position, senderPosition) : 0f;
        if (!PingRules.ShowsForReceiver(signalInterference.IsLocalPlayerInterfered(), signalInterference.IsPartnerInterfered(packet.SessionId), distanceToSender))
        {
            DroppedByInterference++;
            Log.Debug($"Ping {packet.PingId} from {packet.SessionId} not shown: interference, {distanceToSender:0} m from the sender");
            return;
        }

        if (!playerPings.TryGetValue(packet.SessionId, out List<PlayerPing> pings))
        {
            pings = [];
            playerPings[packet.SessionId] = pings;
        }
        RemoveOldestIfFull(pings);

        string senderName = sender.HasValue ? sender.Value.PlayerName : "?";
        Color playerColor = sender.HasValue ? sender.Value.PlayerSettings.PlayerColor.ToUnity() : Color.yellow;
        TechType targetTechType = packet.TargetTechType.ToUnity();
        string label = BuildLabel(packet.Kind, senderName, targetTechType, FindPingedPlayerName(packet.Kind, targetTechType, position, packet.SessionId));

        PlayerPing newPing = PlayerPing.SpawnPlayerPing(packet.Kind, position, senderName, label, targetTechType, packet.PingId, playerColor, IsLocalPlayerAt);
        if (newPing)
        {
            pings.Add(newPing);
        }
    }

    private static void RemoveOldestIfFull(List<PlayerPing> pings)
    {
        pings.RemoveAll(ping => !ping);
        if (pings.Count >= MAX_ACTIVE_PINGS)
        {
            PlayerPing oldestPing = pings[0];
            if (oldestPing)
            {
                Destroy(oldestPing.gameObject);
            }
            pings.RemoveAt(0);
        }
    }

    /// <summary>The label in this game's language, e.g. "Alice: Danger! Stalker", or "Alice: Look! Bob" at a partner.</summary>
    /// <param name="targetName">The pinged player's name, instead of the target's tech type name.</param>
    private static string BuildLabel(PingKind kind, string senderName, TechType targetTechType, string? targetName)
    {
        string target = targetName ?? (targetTechType == TechType.None ? "" : Language.main.Get(targetTechType.AsString()));
        string label = kind switch
        {
            PingKind.Danger => Language.main.GetFormat("Nitrox_Ping_Danger", senderName, target),
            PingKind.Look => Language.main.GetFormat("Nitrox_Ping_Look", senderName, target),
            _ => Language.main.GetFormat("Nitrox_Ping_ComeHere", senderName)
        };
        return label.Trim();
    }

    /// <summary>
    ///     Whom a ping at a player (MULTINAUGHTICA_SPEC M9) points at: the player nearest to the ping other than its
    ///     sender. The packet doesn't name them; with two players it's always the pinged one, the sender's partner. Null
    ///     for any other ping, or when no such player is here (the label then says the game's "Player").
    /// </summary>
    private string? FindPingedPlayerName(PingKind kind, TechType targetTechType, Vector3 pingPosition, SessionId senderId)
    {
        if (kind != PingKind.Look || targetTechType != PingTarget.PLAYER_TECH_TYPE)
        {
            return null;
        }
        string? name = null;
        float nearest = float.MaxValue;
        if (Player.main && localPlayer.SessionId != senderId)
        {
            name = localPlayer.PlayerName;
            nearest = (Player.main.transform.position - pingPosition).sqrMagnitude;
        }
        foreach (RemotePlayer player in playerManager.GetAll())
        {
            if (player.SessionId == senderId || !player.Body)
            {
                continue;
            }
            float distance = (player.Body.transform.position - pingPosition).sqrMagnitude;
            if (distance < nearest)
            {
                name = player.PlayerName;
                nearest = distance;
            }
        }
        return name;
    }

    private static bool IsLocalPlayerAt(Vector3 pingPosition)
    {
        return Player.main && IsNear(Player.main.transform.position, pingPosition);
    }

    private bool IsAnyPartnerAt(Vector3 pingPosition)
    {
        return playerManager.GetAll().Any(partner => partner.Body && IsNear(partner.Body.transform.position, pingPosition));
    }

    private static bool IsNear(Vector3 playerPosition, Vector3 pingPosition)
    {
        return (playerPosition - pingPosition).sqrMagnitude <= PingRules.ARRIVAL_DISTANCE * PingRules.ARRIVAL_DISTANCE;
    }

#if DEBUG
    /// <summary>
    ///     Dev bridge (<c>call PlayerPingManager.Describe</c>): every player ping in this game and what its marker shows.
    /// </summary>
    public static List<Dictionary<string, object?>> Describe()
    {
        uGUI_Pings? pingsUi = FindObjectOfType<uGUI_Pings>();
        List<Dictionary<string, object?>> result = [];
        foreach (PlayerPing ping in FindObjectsOfType<PlayerPing>())
        {
            PingInstance pingInstance = ping.GetComponent<PingInstance>();
            uGUI_Ping? marker = null;
            if (pingsUi && pingInstance && pingInstance.Initialized)
            {
                pingsUi.pings.TryGetValue(pingInstance.Id, out marker);
            }
            Sprite? pingTypeIcon = pingInstance ? SpriteManager.Get(SpriteManager.Group.Pings, PingManager.sCachedPingTypeStrings.Get(pingInstance.pingType)) : null;
            result.Add(new Dictionary<string, object?>
            {
                ["name"] = ping.gameObject.name,
                ["pingId"] = ping.PingId.ToString(),
                ["kind"] = ping.Kind.ToString(),
                ["sender"] = ping.SenderName,
                ["targetTechType"] = ping.TargetTechType.ToString(),
                ["label"] = pingInstance ? pingInstance.GetLabel() : null,
                ["pingType"] = pingInstance ? pingInstance.pingType.ToString() : null,
                ["position"] = new[] { Math.Round(ping.transform.position.x, 1), Math.Round(ping.transform.position.y, 1), Math.Round(ping.transform.position.z, 1) },
                ["age"] = Math.Round(Time.time - ping.SpawnTime, 1),
                ["lifetime"] = ping.Lifetime,
                ["expectedIcon"] = ping.Icon ? ping.Icon.name : null,
                ["pingTypeIcon"] = pingTypeIcon ? pingTypeIcon.name : null,
                ["markerIcon"] = marker && marker.icon && marker.icon.sprite ? marker.icon.sprite.name : null,
                ["markerColor"] = marker ? ColorUtility.ToHtmlStringRGB(marker._iconColor) : null,
                ["expectedColor"] = ColorUtility.ToHtmlStringRGB(ping.Color)
            });
        }
        return result;
    }
#endif
}
