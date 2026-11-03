using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.StoryGates;

namespace Nitrox.Test.Server.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC G0: the story gates' state machine without networking: roster parsing, ready sets, the lock,
///     the presence rule, timeouts and the admin release (L1).
/// </summary>
[TestClass]
public class StoryGateStateTest
{
    private static readonly StoryGateArea room = new("room", new NitroxVector3(0, 0, 0), new NitroxVector3(10, 10, 10));
    private static readonly NitroxVector3 inside = new(5, 5, 5);
    private static readonly NitroxVector3 outside = new(50, 5, 5);
    private static readonly string[] roster = ["Alice", "Bob"];

    private static readonly StoryGateDefinition clickGate = new(StoryGateKind.DISABLE_GUN, StoryGateRule.ALL_READY, TimeSpan.FromSeconds(60));
    private static readonly StoryGateDefinition presenceGate = new(StoryGateKind.DRIVE_CORE, StoryGateRule.ONE_READY_ALL_PRESENT, TimeSpan.FromSeconds(5), room);
    private static readonly StoryGateDefinition lockGate = new(StoryGateKind.SUNBEAM_RADIO, StoryGateRule.ONLINE_ONLY, TimeSpan.FromSeconds(30));

    [TestMethod]
    public void ParseRoster_TakesTheOptionsNames_ElseTheSavedPlayers()
    {
        StoryGateState.ParseRoster(" Alice , bob,, ALICE ", ["Carol"]).Should().Equal("Alice", "bob");
        StoryGateState.ParseRoster("", ["Alice", "Bob", "alice", " "]).Should().Equal("Alice", "Bob");
        StoryGateState.ParseRoster(" , ", ["Carol"]).Should().Equal("Carol");
        StoryGateState.ParseRoster(null, []).Should().BeEmpty();
    }

    [TestMethod]
    public void NobodyReady_DecidesNothing()
    {
        new StoryGateState().Evaluate(clickGate, roster, Online(("Alice", inside), ("Bob", inside)), Seconds(0)).Outcome.Should().Be(StoryGateOutcome.NONE);
    }

    [TestMethod]
    public void AllReady_WaitsForEveryRosterPlayer_ThenStartsOnceAndClears()
    {
        StoryGateState state = new();
        state.SetReady(clickGate.Kind, "Alice", true, Seconds(0));
        StoryGateDecision waiting = state.Evaluate(clickGate, roster, Online(("Alice", inside), ("Bob", inside)), Seconds(1));
        waiting.Outcome.Should().Be(StoryGateOutcome.WAITING);
        waiting.ReadyNames.Should().Equal("Alice");
        waiting.MissingNames.Should().Equal("Bob");

        state.SetReady(clickGate.Kind, "bob", true, Seconds(2));
        StoryGateDecision start = state.Evaluate(clickGate, roster, Online(("Alice", inside), ("Bob", inside)), Seconds(2));
        start.Outcome.Should().Be(StoryGateOutcome.START);
        start.ReadyNames.Should().Equal("Alice", "bob");
        state.HasReady(clickGate.Kind).Should().BeFalse("a start clears the readies, so the next attempt waits again");
    }

    [TestMethod]
    public void RosterPlayerOffline_Locks_NamingThem()
    {
        StoryGateState state = new();
        state.SetReady(clickGate.Kind, "Alice", true, Seconds(0));
        state.SetReady(lockGate.Kind, "Alice", true, Seconds(0));

        StoryGateDecision locked = state.Evaluate(clickGate, roster, Online(("Alice", inside)), Seconds(1));
        locked.Outcome.Should().Be(StoryGateOutcome.LOCKED);
        locked.ReadyNames.Should().Equal("Alice");
        locked.MissingNames.Should().Equal("Bob");
        state.Evaluate(lockGate, roster, Online(("Alice", inside)), Seconds(1)).Outcome.Should().Be(StoryGateOutcome.LOCKED);

        // A player who isn't on the roster neither blocks nor is needed.
        state.Evaluate(lockGate, roster, Online(("Alice", inside), ("Bob", inside), ("Carol", outside)), Seconds(2)).Outcome.Should().Be(StoryGateOutcome.START);
    }

    [TestMethod]
    public void OnlineOnly_StartsAtOnce_WhenEveryoneIsOnline()
    {
        StoryGateState state = new();
        state.SetReady(lockGate.Kind, "Bob", true, Seconds(0));
        state.Evaluate(lockGate, roster, Online(("Alice", outside), ("Bob", inside)), Seconds(0)).Outcome.Should().Be(StoryGateOutcome.START);
    }

    [TestMethod]
    public void Presence_OneAsks_StartsWhenEveryoneIsInTheArea()
    {
        StoryGateState state = new();
        state.SetReady(presenceGate.Kind, "Alice", true, Seconds(0));
        StoryGateDecision waiting = state.Evaluate(presenceGate, roster, Online(("Alice", inside), ("Bob", outside)), Seconds(1));
        waiting.Outcome.Should().Be(StoryGateOutcome.WAITING);
        waiting.MissingNames.Should().Equal("Bob");

        state.SetReady(presenceGate.Kind, "Alice", true, Seconds(2));
        state.Evaluate(presenceGate, roster, Online(("Alice", inside), ("Bob", inside)), Seconds(2)).Outcome.Should().Be(StoryGateOutcome.START);
    }

    [TestMethod]
    public void Ready_ExpiresAfterTheTimeout_AndRenewalKeepsIt()
    {
        StoryGateState state = new();
        state.SetReady(presenceGate.Kind, "Alice", true, Seconds(0));
        state.SetReady(presenceGate.Kind, "Alice", true, Seconds(4));
        state.Evaluate(presenceGate, roster, Online(("Alice", inside), ("Bob", outside)), Seconds(8)).Outcome.Should().Be(StoryGateOutcome.WAITING, "renewed at 4 s");
        state.Evaluate(presenceGate, roster, Online(("Alice", inside), ("Bob", inside)), Seconds(9.5)).Outcome.Should().Be(StoryGateOutcome.NONE, "5 s since the last renewal");
        state.HasReady(presenceGate.Kind).Should().BeFalse();
    }

    [TestMethod]
    public void LeavingTheArea_ClearsTheReady()
    {
        StoryGateState state = new();
        state.SetReady(presenceGate.Kind, "Alice", true, Seconds(0));
        state.Evaluate(presenceGate, roster, Online(("Alice", outside), ("Bob", inside)), Seconds(1)).Outcome.Should().Be(StoryGateOutcome.NONE);
        state.HasReady(presenceGate.Kind).Should().BeFalse();
    }

    [TestMethod]
    public void WithdrawnReady_AndLeaver_AreCleared()
    {
        StoryGateState state = new();
        state.SetReady(clickGate.Kind, "Alice", true, Seconds(0));
        state.SetReady(clickGate.Kind, "Alice", false, Seconds(1));
        state.HasReady(clickGate.Kind).Should().BeFalse();

        state.SetReady(clickGate.Kind, "Alice", true, Seconds(2));
        state.SetReady(presenceGate.Kind, "Alice", true, Seconds(2));
        state.RemovePlayer("ALICE").Should().BeEquivalentTo([clickGate.Kind, presenceGate.Kind]);
        state.KindsWithReady().Should().BeEmpty();
    }

    [TestMethod]
    public void Release_LetsTheNextStartThrough_Once()
    {
        StoryGateState state = new();
        state.Release(clickGate.Kind);
        state.IsReleased(clickGate.Kind).Should().BeTrue();
        state.SetReady(clickGate.Kind, "Alice", true, Seconds(0));
        state.Evaluate(clickGate, roster, Online(("Alice", inside)), Seconds(0)).Outcome.Should().Be(StoryGateOutcome.START, "released, although Bob is offline");
        state.IsReleased(clickGate.Kind).Should().BeFalse();

        state.SetReady(clickGate.Kind, "Alice", true, Seconds(1));
        state.Evaluate(clickGate, roster, Online(("Alice", inside)), Seconds(1)).Outcome.Should().Be(StoryGateOutcome.LOCKED, "the release was used up");
    }

    [TestMethod]
    public void EmptyRoster_NeverWaits()
    {
        StoryGateState state = new();
        state.SetReady(clickGate.Kind, "Alice", true, Seconds(0));
        state.Evaluate(clickGate, [], Online(("Alice", inside)), Seconds(0)).Outcome.Should().Be(StoryGateOutcome.START);
    }

    [TestMethod]
    public void Rules_DriveCoreNeedsPresence_RadioIsALock()
    {
        StoryGateDefinition driveCore = StoryGateRules.For(StoryGateKind.DRIVE_CORE);
        driveCore.Rule.Should().Be(StoryGateRule.ONE_READY_ALL_PRESENT);
        driveCore.Area.Should().Be(StoryGateRules.DriveCoreRoom);
        StoryGateRules.For(StoryGateKind.SUNBEAM_RADIO).Rule.Should().Be(StoryGateRule.ONLINE_ONLY);
        foreach (StoryGateKind kind in Enum.GetValues<StoryGateKind>())
        {
            StoryGateRules.For(kind).Kind.Should().Be(kind);
        }
    }

    [TestMethod]
    public void AquariumPlatform_IsTheTurnedTriggerBox_AsMeasuredInGame()
    {
        // Points checked in game with the trigger's BoxCollider.ClosestPoint (2026-11-03): the first two inside, the third
        // inside the box's axis-aligned bounds but outside the turned box.
        StoryGateArea platform = StoryGateRules.AquariumPlatform;
        platform.Contains(new NitroxVector3(278.42f, -1470.5f, -420.47f)).Should().BeTrue();
        platform.Contains(new NitroxVector3(353.36f, -1470.5f, -456.66f)).Should().BeTrue();
        platform.Contains(new NitroxVector3(311.38f, -1470.5f, -397.2f)).Should().BeFalse();
        platform.Contains(new NitroxVector3(320, -1466, -436)).Should().BeTrue("the game's 'aquarium' teleport spot fires the trigger");
        platform.Contains(new NitroxVector3(335, -1444, -470)).Should().BeFalse("the antechamber is above the platform");
        platform.Contains(new NitroxVector3(315.89f, -1462, -438.566f)).Should().BeFalse("the box is 15 m high");
    }

    [TestMethod]
    public void Area_WithoutYaw_IsAxisAligned_TurnedMovesTheCorners()
    {
        StoryGateArea box = StoryGateArea.Box("room", new NitroxVector3(5, 5, 5), new NitroxVector3(10, 10, 10), 0);
        box.Should().Be(room);
        box.Contains(new NitroxVector3(9.9f, 0.1f, 9.9f)).Should().BeTrue();
        StoryGateArea turned = box with { YawDegrees = 45 };
        turned.Contains(new NitroxVector3(9.9f, 0.1f, 9.9f)).Should().BeFalse("a corner of the box falls outside once it's turned");
        turned.Contains(new NitroxVector3(5, 0.1f, 11.9f)).Should().BeTrue("and a turned corner reaches past the old edge");
    }

    [TestMethod]
    public void Rules_SeaEmperorNeedsBothOnThePlatform_HatchingIsAClick()
    {
        StoryGateDefinition seaEmperor = StoryGateRules.For(StoryGateKind.SEA_EMPEROR);
        seaEmperor.Rule.Should().Be(StoryGateRule.ONE_READY_ALL_PRESENT);
        seaEmperor.Area.Should().Be(StoryGateRules.AquariumPlatform);
        StoryGateRules.For(StoryGateKind.HATCHING).Rule.Should().Be(StoryGateRule.ALL_READY);
        StoryGateRules.For(StoryGateKind.HATCHING).Area.Should().BeNull();
    }

    private static List<StoryGatePlayer> Online(params (string Name, NitroxVector3 Position)[] players) =>
        players.Select(player => new StoryGatePlayer(player.Name, player.Position)).ToList();

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
