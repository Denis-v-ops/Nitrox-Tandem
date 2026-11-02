using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Drives the local player's downed state every frame (COOP_FEATURES_SPEC T1): the countdown in <c>Update</c>, the
///     fade in <c>LateUpdate</c>, after <c>Player.Update</c> wrote the suffocation overlay.
/// </summary>
public class RescueWindowHud : MonoBehaviour
{
    private RescueWindow rescueWindow;

    public void Awake()
    {
        rescueWindow = this.Resolve<RescueWindow>();
    }

    public void Update()
    {
        rescueWindow.Update();
    }

    public void LateUpdate()
    {
        rescueWindow.LateUpdate();
    }
}
