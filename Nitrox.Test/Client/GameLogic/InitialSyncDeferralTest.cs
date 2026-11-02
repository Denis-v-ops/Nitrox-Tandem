using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic.InitialSync;

/// <summary>MULTINAUGHTICA_SPEC M2: story packets that reach a game before its initial sync wait for it.</summary>
[TestClass]
public class InitialSyncDeferralTest
{
    [TestMethod]
    public void BeforeTheSync_PacketsWait_ThenRunInOrder()
    {
        InitialSyncDeferral deferral = new();
        List<string> applied = [];

        deferral.Defer(() => applied.Add("goal 1")).Should().BeTrue();
        deferral.Defer(() => applied.Add("aurora")).Should().BeTrue();
        applied.Should().BeEmpty("the game is still in the menu or loading");

        deferral.MarkSynced();

        applied.Should().Equal("goal 1", "aurora");
    }

    [TestMethod]
    public void AfterTheSync_NothingWaits()
    {
        InitialSyncDeferral deferral = new();
        deferral.MarkSynced();

        deferral.Defer(() => throw new InvalidOperationException("must not be held")).Should().BeFalse();
    }

    [TestMethod]
    public void OneFailingPacket_DoesNotStopTheOthers()
    {
        InitialSyncDeferral deferral = new();
        List<string> applied = [];
        deferral.Defer(() => throw new InvalidOperationException("broken packet"));
        deferral.Defer(() => applied.Add("goal 2"));

        deferral.MarkSynced();

        applied.Should().Equal("goal 2");
    }
}
