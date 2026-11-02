using Nitrox.Model.DataStructures.Unity;
using Nitrox.Server.Subnautica.Models.Resources.Core;

namespace Nitrox.Server.Subnautica.Models.Resources;

/// <param name="PrefabAssets">
/// All attached PrefabPlaceholders (and PrefabPlaceholdersGroup). Is in sync with PrefabPlaceholdersGroup.prefabPlaceholders
/// </param>
/// <param name="PlaceholderParentTransforms">
/// Per entry of <paramref name="PrefabAssets"/>: the transform of the placeholder's parent relative to the group's root, or null
/// when that parent is the root (or for entity slots, whose transform already has the offset). See PlaceholderTransforms.
/// </param>
[Serializable]
public record struct PrefabPlaceholdersGroupAsset(
    string ClassId,
    IPrefabAsset[] PrefabAssets,
    NitroxTransform? Transform = null,
    NitroxTransform?[]? PlaceholderParentTransforms = null
) : IPrefabAsset;
