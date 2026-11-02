using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Gui.InGame;

/// <summary>
///     A player's context ping (COOP_FEATURES_SPEC F2): a <see cref="PingInstance" /> that lasts <see cref="Lifetime" />
///     seconds, or until the partner arrives for a "come here" ping.
/// </summary>
public class PlayerPing : MonoBehaviour
{
    private const float ARRIVAL_CHECK_INTERVAL = 0.25f;

    private Func<Vector3, bool>? hasArrived;
    private float nextArrivalCheck;

    public PingKind Kind { get; private set; }
    public NitroxId PingId { get; private set; } = null!;
    public string SenderName { get; private set; } = "";
    public TechType TargetTechType { get; private set; }
    public float SpawnTime { get; private set; }
    public float Lifetime { get; private set; }

    /// <summary>The item's own sprite for a "look" ping, null to keep the ping type's icon.</summary>
    public Sprite? Icon { get; private set; }

    public Color Color { get; private set; }

    private void Update()
    {
        if (Time.time - SpawnTime >= Lifetime)
        {
            Destroy(gameObject);
            return;
        }
        if (hasArrived != null && Time.time >= nextArrivalCheck)
        {
            nextArrivalCheck = Time.time + ARRIVAL_CHECK_INTERVAL;
            if (hasArrived(transform.position))
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    ///     Gives the ping's marker its own icon and colour. <see cref="uGUI_Pings" /> resets both whenever it adds the
    ///     marker again or the icon changes, so its OnAdd/OnIconChange patches call this too.
    /// </summary>
    public void ApplyTo(uGUI_Ping marker)
    {
        if (Icon)
        {
            marker.SetIcon(Icon);
        }
        marker.SetColor(Color);
    }

    /// <param name="hasArrived">For a "come here" ping: whether the partner is at the given ping position.</param>
    public static PlayerPing SpawnPlayerPing(PingKind kind, Vector3 position, string senderName, string label, TechType targetTechType, NitroxId pingId, Color playerColor,
                                             Func<Vector3, bool>? hasArrived)
    {
        GameObject pingObject = new($"Nitrox_PlayerPing_{kind}_{pingId}");
        pingObject.transform.position = position;

        // Set up before the PingInstance registers: registering adds the marker, and the OnAdd patch reads these.
        PlayerPing playerPing = pingObject.AddComponent<PlayerPing>();
        playerPing.Kind = kind;
        playerPing.PingId = pingId;
        playerPing.SenderName = senderName;
        playerPing.TargetTechType = targetTechType;
        playerPing.SpawnTime = Time.time;
        playerPing.Lifetime = PingRules.Lifetime(kind);
        playerPing.hasArrived = kind == PingKind.ComeHere ? hasArrived : null;
        playerPing.Icon = kind == PingKind.Look && targetTechType != TechType.None ? SpriteManager.Get(SpriteManager.Group.Item, targetTechType.AsString(), null) : null;
        playerPing.Color = kind switch
        {
            PingKind.Danger => Color.red,
            // An item's own icon keeps its colours.
            PingKind.Look when playerPing.Icon => Color.white,
            _ => playerColor
        };

        PingInstance signal = pingObject.AddComponent<PingInstance>();
        signal.pingType = kind == PingKind.ComeHere ? PingType.Beacon : PingType.Signal;
        signal.origin = pingObject.transform;
        signal.minDist = 5f;
        signal._label = label;
        signal.displayPingInManager = false;
        signal.visible = true;
        signal.Initialize();

        FMODEmitterController.PlayEventOneShot(PlayerPingManager.PING_OK_SOUND, 3, Player.main.transform.position);

        return playerPing;
    }
}
