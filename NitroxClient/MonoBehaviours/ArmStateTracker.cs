using System.Collections.Generic;
using Nitrox.Model.GameLogic.PlayerAnimation;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sends the local player's arm and body states (<see cref="ArmStates" />) when they change, so the partner's game shows
///     them on this player's body (COOP_FEATURES_SPEC R2b). Reads what the game itself wrote on the local body's animator,
///     in <c>LateUpdate</c>, after <c>ArmsController.Update</c> and the tools' own updates.
/// </summary>
public class ArmStateTracker : MonoBehaviour
{
    private readonly ArmStateWatch watch = new();
    private LocalPlayer localPlayer = null!;
    private Animator animator = null!;

    public void Awake()
    {
        localPlayer = this.Resolve<LocalPlayer>();
        enabled = false;

        Multiplayer.OnLoadingComplete += Enable;
    }

    public void OnDestroy()
    {
        Multiplayer.OnLoadingComplete -= Enable;
    }

    private void Enable()
    {
        enabled = true;
    }

    public void LateUpdate()
    {
        if (!animator)
        {
            if (!Player.main || !Player.main.armsController)
            {
                return;
            }
            animator = Player.main.armsController.GetComponent<Animator>();
        }
        // An inactive animator has no parameter values to read (and logs a warning for each read)
        if (!animator.isActiveAndEnabled)
        {
            return;
        }

        foreach (KeyValuePair<AnimChangeType, AnimChangeState> change in watch.Update(animator.GetBool, Time.time))
        {
            localPlayer.AnimationChange(change.Key, change.Value);
        }
    }
}
