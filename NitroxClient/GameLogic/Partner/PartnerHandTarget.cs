using UnityEngine;

namespace NitroxClient.GameLogic.Partner;

/// <summary>
///     Makes a partner's body a hand target (COOP_FEATURES_SPEC T2). The body has a trigger capsule on the Useable layer,
///     which <c>GUIHand</c> targets from up to 2 m (<c>Targeting.GetTarget</c>). The reticle shows the best available
///     <see cref="PartnerAction" />; pressing the interact button on the partner starts a hold that lasts while the button
///     stays held and the partner stays targeted, and the action drives the reticle's progress bar.
/// </summary>
public sealed class PartnerHandTarget : MonoBehaviour, IHandTarget
{
    private PartnerAction? holdAction;
    private float heldSeconds;
    private int lastHoverFrame = -10;

    public RemotePlayer Partner { get; private set; } = null!;

    /// <summary>
    ///     The partner's oxygen from their last <c>PlayerStats</c> (sent every 3 s).
    /// </summary>
    public float Oxygen { get; private set; }

    /// <summary>
    ///     The partner's oxygen capacity from their last <c>PlayerStats</c>; 0 until the first arrives.
    /// </summary>
    public float MaxOxygen { get; private set; }

    /// <summary>
    ///     Whether the partner is down and waiting for a rescue (COOP_FEATURES_SPEC T1 sets it).
    /// </summary>
    public bool IsDowned { get; set; }

    /// <summary>
    ///     True while the local player holds an action on this partner.
    /// </summary>
    public bool IsHolding => holdAction != null;

    public static PartnerHandTarget AddTo(RemotePlayer partner)
    {
        PartnerHandTarget handTarget = partner.Body!.AddComponent<PartnerHandTarget>();
        handTarget.Partner = partner;
        return handTarget;
    }

    public void SetOxygen(float oxygen, float maxOxygen)
    {
        Oxygen = oxygen;
        MaxOxygen = maxOxygen;
    }

    public void OnHandHover(GUIHand hand)
    {
        // GUIHand hovers every frame while the body is targeted, so a gap means the player looked away.
        bool hoveredLastFrame = lastHoverFrame == Time.frameCount - 1;
        lastHoverFrame = Time.frameCount;

        PartnerAction? action = FirstAvailableAction();
        if (action == null)
        {
            holdAction = null;
            HandReticle.main.SetText(HandReticle.TextType.Hand, Partner.PlayerName, false);
            HandReticle.main.SetText(HandReticle.TextType.HandSubscript, string.Empty, false);
            return;
        }

        HandReticle.main.SetText(HandReticle.TextType.Hand, action.GetText(this), false, GameInput.Button.LeftHand);
        HandReticle.main.SetText(HandReticle.TextType.HandSubscript, action.GetSubscript(this), false);
        if (holdAction != action || !hoveredLastFrame || !GameInput.GetButtonHeld(GameInput.Button.LeftHand))
        {
            holdAction = null;
            HandReticle.main.SetIcon(HandReticle.IconType.Hand);
            return;
        }

        heldSeconds += Time.deltaTime;
        if (!action.OnHeld(this, heldSeconds, Time.deltaTime, out float progress))
        {
            holdAction = null;
        }
        HandReticle.main.SetProgress(Mathf.Clamp01(progress));
        HandReticle.main.SetIcon(HandReticle.IconType.Progress);
    }

    /// <summary>
    ///     Called by <c>GUIHand</c> when the interact button goes down on the body (after this frame's hover).
    /// </summary>
    public void OnHandClick(GUIHand hand)
    {
        holdAction = FirstAvailableAction();
        heldSeconds = 0;
    }

    private PartnerAction? FirstAvailableAction()
    {
        PartnerActions? actions = this.Resolve<PartnerActions>();
        return actions == null ? null : actions.FirstAvailable(this);
    }
}
