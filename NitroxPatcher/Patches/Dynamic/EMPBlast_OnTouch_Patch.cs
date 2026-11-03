using System.Reflection;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     M4 (FIX_SPEC S15a): every game casts a Crabsquid's EMP blast at its own copy (<see cref="EMPAttack_CastEMPBlast_Patch" />),
///     so each player's own game disables their own tool. A blast touching another player's body is skipped: for a target
///     tagged "Player" the game disables <c>Inventory.main</c>'s held tool, which is the local player's
///     (<c>EMPBlast.OnTouch</c>, game 83031), so it would disable this player's tool when only the partner was in range.
///     Whether the body (a copy of the player's "body" object) carries that tag is asset data and wasn't read; the body
///     has no tool or vehicle of its own to disable, so skipping it is right either way.
/// </summary>
public sealed partial class EMPBlast_OnTouch_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((EMPBlast t) => t.OnTouch(default));

    public static bool Prefix(Collider collider)
    {
        return !collider || !collider.attachedRigidbody || !collider.attachedRigidbody.GetComponent<RemotePlayerIdentifier>();
    }
}
