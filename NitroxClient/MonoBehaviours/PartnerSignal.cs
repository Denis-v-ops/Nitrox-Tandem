using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Settings;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sits on a partner's body and sets how their marker and name tag look, about four times a second
///     (COOP_FEATURES_SPEC F1). The look comes from <see cref="PartnerSignalRules" />. Only fields of the marker are
///     changed: <c>SetVisible</c>/<c>SetColor</c> are sent to the server as the player's ping preference.
/// </summary>
public class PartnerSignal : MonoBehaviour
{
    private const float UPDATE_INTERVAL = 0.25f;

    private RemotePlayer remotePlayer;
    private PingInstance ping;
    private float defaultMinDist;
    private float defaultRange;
    private float nextUpdateTime;
    private int labelDepth = -1;

    /// <summary>
    ///     From the local camera to the partner's marker, in metres.
    /// </summary>
    public float Distance { get; private set; }

    public bool InLineOfSight { get; private set; }

    public PartnerSignalLook Look { get; private set; }

    /// <summary>
    ///     For the dev bridge: the marker's distances as set, and whether the name tag shows.
    /// </summary>
    public string Rule => Look.Rule;
    public float MinDist => ping ? ping.minDist : -1f;
    public float Range => ping ? ping.range : -1f;
    public bool NameTagVisible => remotePlayer != null && remotePlayer.vitals && remotePlayer.vitals.NameTagVisible;
    public string Label => ping ? ping.GetLabel() : null;

    public void Initialize(RemotePlayer player)
    {
        remotePlayer = player;
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
        Vector3 partner = ping.GetPosition();
        Distance = Vector3.Distance(eye, partner);
        // Terrain only, as creatures check their sight (Creature.GetCanSeeObject): base walls don't block it
        InLineOfSight = Distance < FaintSignalRule.LINE_OF_SIGHT_DIST && !Physics.Linecast(eye, partner, Voxeland.GetTerrainLayerMask(), QueryTriggerInteraction.Ignore);

        PartnerSignalContext context = new((PartnerSignalMode)NitroxPrefs.PartnerSignal.Value, Distance, InLineOfSight, NitroxPrefs.NameTagRange.Value, defaultMinDist, defaultRange);
        Look = PartnerSignalRules.GetLook(context);

        ping.minDist = Look.MinDist;
        ping.range = Look.Range;
        if (remotePlayer.vitals)
        {
            remotePlayer.vitals.SetNameTagVisible(Look.NameTagVisible);
        }
        UpdateLabel(partner);
    }

    private bool TryFindPing()
    {
        if (ping)
        {
            return true;
        }
        // Attached by a coroutine (PlayerModelManager.AttachPing), so it may not exist yet
        ping = remotePlayer.PlayerModel.GetComponentInChildren<PingInstance>(true);
        if (!ping)
        {
            return false;
        }
        defaultMinDist = ping.minDist;
        defaultRange = ping.range;
        return true;
    }

    private void UpdateLabel(Vector3 partner)
    {
        int depth = Mathf.RoundToInt(Ocean.GetDepthOf(partner));
        if (depth == labelDepth)
        {
            return;
        }
        labelDepth = depth;
        // SetLabel isn't synced, unlike SetVisible/SetColor
        ping.SetLabel(depth < 1
                          ? remotePlayer.PlayerName
                          : Language.main.Get("Nitrox_PartnerSignalLabel").Replace("{PLAYER}", remotePlayer.PlayerName).Replace("{DEPTH}", depth.ToString()));
    }
}
