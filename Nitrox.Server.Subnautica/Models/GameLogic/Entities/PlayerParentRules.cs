using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     A player entity's parent is a sub root, an escape pod or nothing, and two packets write it: <c>SubRootChanged</c> and
///     <c>EscapePodChanged</c>. Each speaks only for its own kind of parent (MULTINAUGHTICA_SPEC M2):
///     <list type="bullet">
///         <item>
///             "No sub root" doesn't take a player out of an escape pod. A pod is no sub root, and a respawn in the pod
///             sends the pod (<c>EscapePod_RespawnPlayer_Patch</c>) and then "no sub root"
///             (<c>Player.MovePlayerToRespawnPoint</c> calls <c>SetCurrentSub(null)</c>, then <c>Player_MovePlayerToRespawnPoint_Patch</c>).
///         </item>
///         <item>
///             "No escape pod" doesn't take a player out of a sub root: the game drops its pod once the player is far
///             enough from it (<c>Player</c>'s escape pod radius check), wherever they are, e.g. after a warp into a base.
///         </item>
///     </list>
/// </summary>
internal static class PlayerParentRules
{
    /// <returns>True if the packet says "no sub root" while the player is in an escape pod: the parent stays the pod.</returns>
    public static bool KeepsEscapePodParent(Optional<NitroxId> newSubRootId, Entity? currentParent) => !newSubRootId.HasValue && currentParent is EscapePodEntity;

    /// <returns>True if the packet says "no escape pod" while the player is in a sub root: the parent stays the sub root.</returns>
    public static bool KeepsSubRootParent(Optional<NitroxId> newEscapePodId, Entity? currentParent) => !newEscapePodId.HasValue && currentParent is not null and not EscapePodEntity;

    public static Entity? GetParent(EntityRegistry entityRegistry, NitroxId playerEntityId)
    {
        return entityRegistry.TryGetEntityById(playerEntityId, out Entity player) && player.ParentId != null && entityRegistry.TryGetEntityById(player.ParentId, out Entity parent)
            ? parent
            : null;
    }
}
