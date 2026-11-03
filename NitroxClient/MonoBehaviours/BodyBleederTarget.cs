using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     FIX_SPEC S15d: where a bleeder latches onto a partner's body (see <see cref="Bleeders" />). The body is a copy of
///     the local player's, so it carries its own <see cref="BleederAttachTarget" />; that copy's <c>player</c> still
///     points at the local player (a reference outside the copied objects), which would make <c>AttachAndSuck.Update</c>
///     let go of the partner when the local player walks into a base, so it is cleared. The body's collider is a trigger,
///     so a bleeder never collides with it: its touch arrives here instead.
/// </summary>
public sealed class BodyBleederTarget : MonoBehaviour
{
    public RemotePlayer Partner { get; private set; } = null!;
    public BleederAttachTarget Target { get; private set; } = null!;

    public static BodyBleederTarget AddTo(RemotePlayer partner)
    {
        BodyBleederTarget bodyTarget = partner.Body!.AddComponent<BodyBleederTarget>();
        bodyTarget.Partner = partner;
        bodyTarget.Target = partner.Body.GetComponentInChildren<BleederAttachTarget>(true);
        if (!bodyTarget.Target)
        {
            bodyTarget.Target = CreateTarget(partner);
        }
        bodyTarget.Target.player = null;
        return bodyTarget;
    }

    public void OnTriggerEnter(Collider other)
    {
        GameObject bleederObject = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        if (bleederObject.TryGetComponent(out AttachAndSuck attachAndSuck))
        {
            this.Resolve<Bleeders>().TryAttachToBody(attachAndSuck, this);
        }
    }

    /// <summary>
    ///     Fallback if the body came without a target: one where the local player's is, relative to the player model.
    /// </summary>
    private static BleederAttachTarget CreateTarget(RemotePlayer partner)
    {
        Log.Warn($"[{nameof(BodyBleederTarget)}] {partner.PlayerName}'s body has no {nameof(BleederAttachTarget)}, adding one");
        GameObject targetObject = new(nameof(BleederAttachTarget));
        targetObject.transform.SetParent(partner.PlayerModel.transform, false);
        ArmsController localArms = Player.main ? Player.main.armsController : null;
        if (localArms && localArms.bleederAttackTarget)
        {
            Transform local = localArms.bleederAttackTarget.transform;
            targetObject.transform.localPosition = localArms.transform.InverseTransformPoint(local.position);
            targetObject.transform.localRotation = Quaternion.Inverse(localArms.transform.rotation) * local.rotation;
        }
        return targetObject.AddComponent<BleederAttachTarget>();
    }
}
