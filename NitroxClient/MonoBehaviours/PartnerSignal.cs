using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Settings;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sits on a partner's body and sets how their marker and name tag look, about four times a second
///     (COOP_FEATURES_SPEC F1, D2). The look comes from <see cref="PartnerSignalRules" />. Only fields of the marker are
///     changed: <c>SetVisible</c>/<c>SetColor</c> are sent to the server as the player's ping preference.
/// </summary>
/// <remarks>
///     While the line is cut (D2: either player in signal interference), the marker's <c>origin</c> points at a static
///     object where contact was lost, so the marker stays there; when contact returns it follows the body again.
/// </remarks>
public class PartnerSignal : MonoBehaviour
{
    private const float UPDATE_INTERVAL = 0.25f;

    private RemotePlayer remotePlayer;
    private SignalInterference signalInterference;
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

    public void Initialize(RemotePlayer player)
    {
        remotePlayer = player;
        signalInterference = this.Resolve<SignalInterference>();
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

        PartnerSignalContext context = new((PartnerSignalMode)NitroxPrefs.PartnerSignal.Value, Distance, InLineOfSight, NitroxPrefs.NameTagRange.Value, defaultMinDist, defaultRange,
                                           LinkCut, SecondsSinceContact);
        Look = PartnerSignalRules.GetLook(context);

        if (Look.Frozen)
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
        if (Look.Frozen)
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
