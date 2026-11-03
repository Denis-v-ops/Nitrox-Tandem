using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Drives the local player's Seamoth hitch every frame (COOP_FEATURES_SPEC T5): the release checks and the "let go"
///     line in <c>Update</c>, the ride in <c>LateUpdate</c>, after this game's copy of the Seamoth moved.
/// </summary>
public class SeamothHitchDriver : MonoBehaviour
{
    private SeamothHitch seamothHitch;

    public void Awake()
    {
        seamothHitch = this.Resolve<SeamothHitch>();
    }

    public void Update()
    {
        seamothHitch.Update();
    }

    public void LateUpdate()
    {
        seamothHitch.LateUpdate();
    }
}
