namespace NitroxClient.GameLogic;

[TestClass]
public class BaseFloodLevelsTest
{
    [TestMethod]
    public void SnapshotOfDryBaseIsEmpty()
    {
        BaseFloodLevels.Snapshot([0f, 0.001f, 0f]).Should().BeEmpty();
        BaseFloodLevels.Snapshot(null).Should().BeEmpty();
    }

    [TestMethod]
    public void SnapshotIsACopy()
    {
        float[] levels = [0f, 0.5f, 1f];
        float[] snapshot = BaseFloodLevels.Snapshot(levels);
        levels[1] = 0.75f;
        snapshot.Should().Equal(0f, 0.5f, 1f);
    }

    [TestMethod]
    public void FirstSnapshotCountsAsChanged()
    {
        BaseFloodLevels.HasChanged(null, []).Should().BeTrue();
    }

    [TestMethod]
    public void TinyDriftIsNotAChange()
    {
        BaseFloodLevels.HasChanged([0f, 0.5f], [0f, 0.505f]).Should().BeFalse();
        BaseFloodLevels.HasChanged([], []).Should().BeFalse();
    }

    [TestMethod]
    public void RisingDrainingOrResizedIsAChange()
    {
        BaseFloodLevels.HasChanged([0f, 0.5f], [0f, 0.52f]).Should().BeTrue();
        BaseFloodLevels.HasChanged([0f, 0.5f], []).Should().BeTrue("the base drained");
        BaseFloodLevels.HasChanged([0f, 0.5f], [0f, 0.5f, 0f]).Should().BeTrue("the grid was resized");
    }
}
