namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: a building resync brings back a base or module the game lost. Pairing it with a null
///     component made the overwrite throw, and the entity was dropped before the "spawn missing" loop.
/// </summary>
[TestClass]
public class BuildingResyncPairingTest
{
    private sealed record FakeEntity(string Name, int Position);

    private sealed record FakeComponent(string Name, int Position);

    private static bool SamePosition(FakeEntity entity, FakeComponent component) => entity.Position == component.Position;

    [TestMethod]
    public void EntityWithoutComponent_IsNotPaired()
    {
        FakeEntity lostBase = new("base", 10);
        List<FakeComponent> unmarked = [];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { lostBase }, unmarked, SamePosition);

        pairs.Should().BeEmpty();
    }

    [TestMethod]
    public void EntityWithCorrespondingComponent_IsPairedAndTheComponentIsMarked()
    {
        FakeEntity entity = new("base", 10);
        FakeComponent component = new("component", 10);
        List<FakeComponent> unmarked = [new("other", 99), component];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { entity }, unmarked, SamePosition);

        pairs.Should().ContainSingle().Which.Should().Be((entity, component));
        unmarked.Should().ContainSingle().Which.Name.Should().Be("other");
    }

    [TestMethod]
    public void OneComponentTwoEntities_OnlyOneEntityIsPaired()
    {
        FakeEntity first = new("first", 10);
        FakeEntity second = new("second", 10);
        FakeComponent component = new("component", 10);
        List<FakeComponent> unmarked = [component];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { first, second }, unmarked, SamePosition);

        pairs.Should().ContainSingle().Which.Component.Should().Be(component);
        unmarked.Should().BeEmpty();
    }

    [TestMethod]
    public void MixedList_PairsOnlyTheEntitiesThatHaveAComponent()
    {
        FakeEntity kept = new("kept", 1);
        FakeEntity lost = new("lost", 2);
        FakeEntity alsoKept = new("alsoKept", 3);
        List<FakeComponent> unmarked = [new("keptComponent", 1), new("alsoKeptComponent", 3)];

        List<(FakeEntity Entity, FakeComponent Component)> pairs = BuildingResyncProcessor.PairWithComponents(new List<FakeEntity> { kept, lost, alsoKept }, unmarked, SamePosition);

        pairs.Select(pair => pair.Entity).Should().BeEquivalentTo([kept, alsoKept]);
        pairs.Should().OnlyContain(pair => pair.Component != null);
        unmarked.Should().BeEmpty();
    }
}
