using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Settings;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sits on a partner's body and sets how their marker and name tag look, about four times a second
///     (COOP_FEATURES_SPEC F1, D2, D3). The look comes from <see cref="PartnerSignalRules" />. Only fields of the marker are
///     changed: <c>SetVisible</c>/<c>SetColor</c> are sent to the server as the player's ping preference.
/// </summary>
/// <remarks>
///     While the line is cut (D2: either player in signal interference), the marker's <c>origin</c> points at a static
///     object where contact was lost, so the marker stays there; when contact returns it follows the body again.
///     While the partner is down (D3) the marker turns red and jumps to their real position every
///     <see cref="DistressRule.PULSE_SECONDS" />, with a short signal sound, through interference too.
/// </remarks>
public class PartnerSignal : MonoBehaviour
{
    private const float UPDATE_INTERVAL = 0.25f;

    /// <summary>
    ///     The distress pulse's sound: the short ping of the game's metal detector (<c>LootSensor</c>), played for the local
    ///     player only (it isn't on Nitrox's sound whitelist).
    /// </summary>
    public const string DISTRESS_PULSE_SOUND = "event:/interface/ping";

    private RemotePlayer remotePlayer;
    private SignalInterference signalInterference;
    private RescueWindow rescueWindow;
    private uGUI_Pings pings;
    private GameObject distressAnchor;
    private float? lastPulseTime;
    private int defaultColorIndex;
    private float distressSecondsLeft;
    private PingInstance ping;
    private Transform bodyOrigin;
    private GameObject lastContact;
    private float defaultMinDist;
    private float defaultRange;
    private float nextUpdateTime;
    private float lastContactTime;
    private bool hasContactPosition;
    private Vector3 lastContactPosition;
    private string label;

    /// <summary>
    ///     From the local camera to the partner's body, in metres.
    /// </summary>
    public float Distance { get; private set; }

    public bool InLineOfSight { get; private set; }

    /// <summary>
    ///     Either player is in signal interference (COOP_FEATURES_SPEC D2).
    /// </summary>
    public bool LinkCut { get; private set; }

    /// <summary>
    ///     Seconds since the marker last followed the partner.
    /// </summary>
    public float SecondsSinceContact { get; private set; }

    public PartnerSignalLook Look { get; private set; }

    /// <summary>
    ///     For the dev bridge: the marker's distances and position as set, and whether the name tag shows.
    /// </summary>
    public string Rule => Look.Rule;
    public bool Frozen => Look.Frozen;
    public float MinDist => ping ? ping.minDist : -1f;
    public float Range => ping ? ping.range : -1f;
    public Vector3 MarkerPosition => ping ? ping.GetPosition() : Vector3.zero;
    public bool NameTagVisible => remotePlayer != null && remotePlayer.vitals && remotePlayer.vitals.NameTagVisible;
    public string Label => ping ? ping.GetLabel() : null;

    /// <summary>
    ///     For the dev bridge: distress pulses so far, and where the last one put the marker (COOP_FEATURES_SPEC D3).
    /// </summary>
    public int DistressPulses { get; private set; }

    public Vector3 LastPulsePosition { get; private set; }
    public int ColorIndex => ping ? ping.colorIndex : -1;

    public void Initialize(RemotePlayer player)
    {
        remotePlayer = player;
        signalInterference = this.Resolve<SignalInterference>();
        rescueWindow = this.Resolve<RescueWindow>();
        lastContactTime = Time.unscaledTime;
    }

    public void Update()
    {
        if (Time.unscaledTime < nextUpdateTime)
        {
            return;
        }
        nextUpdateTime = Time.unscaledTime + UPDATE_INTERVAL;
        Refresh();
    }

    public void Refresh()
    {
        if (remotePlayer == null || !TryFindPing())
        {
            return;
        }

        Vector3 eye = MainCamera.camera ? MainCamera.camera.transform.position : Player.main.transform.position;
        Vector3 partner = bodyOrigin.position;
        Distance = Vector3.Distance(eye, partner);
        // Terrain only, as creatures check their sight (Creature.GetCanSeeObject): base walls don't block it
        InLineOfSight = Distance < FaintSignalRule.LINE_OF_SIGHT_DIST && !Physics.Linecast(eye, partner, Voxeland.GetTerrainLayerMask(), QueryTriggerInteraction.Ignore);
        LinkCut = signalInterference != null && signalInterference.IsLinkCut(remotePlayer.SessionId);
        SecondsSinceContact = Time.unscaledTime - lastContactTime;
        bool distress = rescueWindow != null && rescueWindow.TryGetPartnerSecondsLeft(remotePlayer.SessionId, out distressSecondsLeft);

        PartnerSignalContext context = new((PartnerSignalMode)NitroxPrefs.PartnerSignal.Value, Distance, InLineOfSight, NitroxPrefs.NameTagRange.Value, defaultMinDist, defaultRange,
                                           LinkCut, SecondsSinceContact, distress);
        bool wasDistress = IsDistress;
        Look = PartnerSignalRules.GetLook(context);
        if (wasDistress && !IsDistress)
        {
            EndDistress();
        }

        if (IsDistress)
        {
            ping.origin = PulseDistress(partner);
        }
        else if (Look.Frozen)
        {
            ping.origin = GetLastContact();
        }
        else
        {
            // The marker follows the partner: that is contact
            ping.origin = bodyOrigin;
            lastContactPosition = partner;
            hasContactPosition = true;
            lastContactTime = Time.unscaledTime;
            SecondsSinceContact = 0;
        }
        ping.minDist = Look.MinDist;
        ping.range = Look.Range;
        if (remotePlayer.vitals)
        {
            remotePlayer.vitals.SetNameTagVisible(Look.NameTagVisible);
        }
        UpdateLabel(partner);
    }

    public void OnDestroy()
    {
        if (lastContact)
        {
            Destroy(lastContact);
        }
        if (distressAnchor)
        {
            Destroy(distressAnchor);
        }
    }

    public bool IsDistress => Look.Rule == DistressRule.NAME;

    /// <summary>
    ///     The distress marker's anchor, moved to the partner's position when a pulse is due. A pulse is contact too: if the
    ///     line is still cut when the distress call ends (D2), the last contact is the last pulse.
    /// </summary>
    private Transform PulseDistress(Vector3 partner)
    {
        if (!distressAnchor)
        {
            distressAnchor = new GameObject($"distress_{remotePlayer.PlayerName}");
        }
        float now = Time.unscaledTime;
        if (DistressRule.IsPulseDue(lastPulseTime, now))
        {
            if (!lastPulseTime.HasValue)
            {
                defaultColorIndex = ping.colorIndex;
            }
            lastPulseTime = now;
            distressAnchor.transform.position = partner;
            LastPulsePosition = partner;
            DistressPulses++;
            lastContactPosition = partner;
            hasContactPosition = true;
            lastContactTime = now;
            SetMarkerColor(DistressRule.COLOR_INDEX, PingManager.colorOptions[DistressRule.COLOR_INDEX]);
            FMODUWE.PlayOneShotImpl(DISTRESS_PULSE_SOUND, MainCamera.camera ? MainCamera.camera.transform.position : partner, 1f);
        }
        return distressAnchor.transform;
    }

    private void EndDistress()
    {
        lastPulseTime = null;
        SetMarkerColor(defaultColorIndex, remotePlayer.PlayerSettings.PlayerColor.ToUnity());
    }

    /// <summary>
    ///     As <c>PlayerModelManager</c> colours the marker: straight to <c>uGUI_Pings.OnColor</c>, not
    ///     <c>PingInstance.SetColor</c>, which Nitrox sends to the server as the player's preference. <c>uGUI_Pings</c>
    ///     reads <c>colorIndex</c> only when it adds a ping (also when it is enabled again), so the field is set too.
    /// </summary>
    private void SetMarkerColor(int colorIndex, Color color)
    {
        ping.colorIndex = colorIndex;
        if (!pings)
        {
            pings = FindObjectOfType<uGUI_Pings>();
        }
        if (pings)
        {
            pings.OnColor(ping.Id, color);
        }
    }

    private bool TryFindPing()
    {
        if (ping)
        {
            return true;
        }
        // Attached by a coroutine (PlayerModelManager.AttachPing), so it may not exist yet. Only that one: a beacon the
        // partner carries has its own PingInstance under the body too.
        foreach (PingInstance candidate in remotePlayer.PlayerModel.GetComponentsInChildren<PingInstance>(true))
        {
            if (PartnerSignalRules.IsPartnerMarker(candidate.name, remotePlayer.PlayerName))
            {
                ping = candidate;
                break;
            }
        }
        if (!ping)
        {
            return false;
        }
        defaultMinDist = ping.minDist;
        defaultRange = ping.range;
        bodyOrigin = ping.origin ? ping.origin : ping.transform;
        return true;
    }

    /// <summary>
    ///     A static object where the marker last followed the partner (where they were first seen, if never).
    /// </summary>
    private Transform GetLastContact()
    {
        if (!hasContactPosition)
        {
            lastContactPosition = bodyOrigin.position;
            hasContactPosition = true;
        }
        if (!lastContact)
        {
            lastContact = new GameObject($"last_contact_{remotePlayer.PlayerName}");
        }
        lastContact.transform.position = lastContactPosition;
        return lastContact.transform;
    }

    private void UpdateLabel(Vector3 partner)
    {
        string text;
        if (IsDistress)
        {
            text = Language.main.Get("Nitrox_PartnerSignalDistress").Replace("{PLAYER}", remotePlayer.PlayerName).Replace("{SECONDS}", DistressRule.SecondsLabel(distressSecondsLeft).ToString());
        }
        else if (Look.Frozen)
        {
            (int age, bool minutes) = LastContactRule.Age(SecondsSinceContact);
            string format = minutes ? Language.main.Get("Nitrox_PartnerSignalLastContactMinutes") : Language.main.Get("Nitrox_PartnerSignalLastContactSeconds");
            text = format.Replace("{PLAYER}", remotePlayer.PlayerName).Replace("{AGE}", age.ToString());
        }
        else
        {
            int depth = Mathf.RoundToInt(Ocean.GetDepthOf(partner));
            text = depth < 1
                       ? remotePlayer.PlayerName
                       : Language.main.Get("Nitrox_PartnerSignalLabel").Replace("{PLAYER}", remotePlayer.PlayerName).Replace("{DEPTH}", depth.ToString());
        }
        if (text == label)
        {
            return;
        }
        label = text;
        // SetLabel isn't synced, unlike SetVisible/SetColor
        ping.SetLabel(text);
    }
}
