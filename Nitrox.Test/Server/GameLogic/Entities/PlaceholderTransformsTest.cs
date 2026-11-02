using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

namespace Nitrox.Test.Server.GameLogic.Entities;

/// <summary>
///     Placeholders under an intermediate object of their group (FIX_SPEC log 2026-11-02, finding 2): the server must put
///     their entities where the game does, not about 1.4 km away.
/// </summary>
[TestClass]
public class PlaceholderTransformsTest
{
    private static readonly NitroxVector3 one = new(1, 1, 1);

    [TestMethod]
    public void NoChainMeansNoIntermediate()
    {
        PlaceholderTransforms.ComposeParentChain([]).Should().BeNull();
    }

    [TestMethod]
    public void ComposedChainGivesTheSamePositionAsTheFullHierarchy()
    {
        NitroxTransform group = new(new NitroxVector3(67, -25, 394), NitroxQuaternion.FromEuler(0, 40, 0), one);
        // Hierarchy in the prefab: group root > outer (rotated, scaled) > inner > placeholder
        NitroxTransform outer = new(new NitroxVector3(10, 2, -3), NitroxQuaternion.FromEuler(0, 90, 0), new NitroxVector3(2, 2, 2));
        NitroxTransform inner = new(new NitroxVector3(0, 1, 5), NitroxQuaternion.FromEuler(10, 0, 0), one);
        NitroxVector3 childLocal = new(1, 2, 3);

        outer.SetParent(group, false);
        inner.SetParent(outer, false);
        NitroxTransform childInFullHierarchy = new(childLocal, NitroxQuaternion.Identity, one);
        childInFullHierarchy.SetParent(inner, false);

        NitroxTransform? composed = PlaceholderTransforms.ComposeParentChain([
            new NitroxTransform(inner.LocalPosition, inner.LocalRotation, inner.LocalScale),
            new NitroxTransform(outer.LocalPosition, outer.LocalRotation, outer.LocalScale)
        ]);
        NitroxTransform child = new(childLocal, NitroxQuaternion.Identity, one);
        PlaceholderTransforms.SetParent(child, group, composed);

        AssertClose(childInFullHierarchy.Position, child.Position);
        child.LocalPosition.Should().Be(childLocal, "the client applies the local position under the placeholder's real parent");
    }

    [TestMethod]
    public void PlaceholderUnderOffsetIntermediateLandsNextToItsGroup()
    {
        // Values of group 34d3f7a1-3516-43c6-99f9-bf4cccc3a30b (placeholder 4) and the KelpForest_17 group seen in game
        NitroxVector3 groupPosition = new(67, -25, 394);
        NitroxTransform groupTransform = new(groupPosition, NitroxQuaternion.Identity, one);
        WorldEntity group = new(groupPosition, NitroxQuaternion.Identity, one, NitroxTechType.None, 0, "group", true, new NitroxId(), null);
        group.Transform = groupTransform;
        NitroxVector3 placeholderLocal = new(-782.2764f, 418.35577f, -1081.1929f);
        NitroxTransform intermediate = new(new NitroxVector3(780f, -419f, 1082f), NitroxQuaternion.Identity, one);

        WorldEntity before = new(placeholderLocal, NitroxQuaternion.Identity, one, NitroxTechType.None, 0, "bench", true, new NitroxId(), group);
        WorldEntity after = new(placeholderLocal, NitroxQuaternion.Identity, one, NitroxTechType.None, 0, "bench", true, new NitroxId(), group);
        PlaceholderTransforms.SetParent(after.Transform, group.Transform, intermediate);

        Distance(before.Transform.Position, groupPosition).Should().BeGreaterThan(1000, "without the intermediate the server is about 1.4 km off");
        Distance(after.Transform.Position, groupPosition).Should().BeLessThan(5);
        after.AbsoluteEntityCell.Should().Be(new AbsoluteEntityCell(groupPosition + new NitroxVector3(-2.2764f, -0.64423f, 0.8071f), 0));
    }

    private static float Distance(NitroxVector3 a, NitroxVector3 b) => NitroxVector3.Distance(a, b);

    private static void AssertClose(NitroxVector3 expected, NitroxVector3 actual)
    {
        Distance(expected, actual).Should().BeLessThan(0.001f, $"expected {expected}, got {actual}");
    }
}
