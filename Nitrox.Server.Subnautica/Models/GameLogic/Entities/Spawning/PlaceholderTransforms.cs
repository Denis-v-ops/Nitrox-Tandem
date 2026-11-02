using System.Collections.Generic;
using System.Numerics;
using Nitrox.Model.DataStructures.Unity;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

/// <summary>
///     Transform maths for objects spawned by a prefab placeholder.
/// </summary>
/// <remarks>
///     The game spawns a placeholder's object under the placeholder's own parent with the placeholder's local transform
///     (<c>PrefabPlaceholder.Spawn</c>, game 83031). That parent isn't always the group's root: some prefabs put their
///     placeholders under an intermediate object (e.g. 11 placeholders of group <c>34d3f7a1-…</c>, a part of the
///     KelpForest wrecks, sit at about (-780, 419, -1082) under an intermediate object that cancels that offset).
///     Entities keep that local transform, because the client applies it under the same parent
///     (<c>PrefabPlaceholderEntitySpawner</c>). The server only needs the intermediate transform to compute the world
///     position, and with it the cell, of such an entity.
/// </remarks>
internal static class PlaceholderTransforms
{
    /// <summary>
    ///     Combines the local transforms of a placeholder's ancestors, from its direct parent up to (not including) the
    ///     group's root, into the transform of that parent relative to the root. Null when the parent is the root.
    /// </summary>
    public static NitroxTransform? ComposeParentChain(IReadOnlyList<NitroxTransform> parentToRootChain)
    {
        if (parentToRootChain.Count == 0)
        {
            return null;
        }

        // Row vectors like NitroxTransform.LocalToWorldMatrix: the child's matrix comes first
        Matrix4x4 matrix = Matrix4x4.Identity;
        foreach (NitroxTransform transform in parentToRootChain)
        {
            matrix *= Matrix4x4Extension.Compose(transform.LocalPosition, transform.LocalRotation, transform.LocalScale);
        }
        Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation);
        return new NitroxTransform((NitroxVector3)translation, (NitroxQuaternion)rotation, (NitroxVector3)scale);
    }

    /// <summary>
    ///     Parents the transform of a placeholder's entity to its group's transform, through a copy of the placeholder's
    ///     parent transform (relative to the group root) when there is one. Local values are kept.
    /// </summary>
    public static void SetParent(NitroxTransform child, NitroxTransform groupTransform, NitroxTransform? placeholderParent)
    {
        if (placeholderParent == null)
        {
            child.SetParent(groupTransform, false);
            return;
        }

        NitroxTransform intermediate = new(placeholderParent.LocalPosition, placeholderParent.LocalRotation, placeholderParent.LocalScale);
        intermediate.SetParent(groupTransform, false);
        child.SetParent(intermediate, false);
    }
}
