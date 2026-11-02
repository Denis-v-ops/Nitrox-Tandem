using Nitrox.Model.GameLogic.PlayerAnimation;

namespace NitroxClient.GameLogic.PlayerLogic;

/// <summary>
///     COOP_FEATURES_SPEC R2b: which arm states are sent when, and which ones the server replays to late joiners.
/// </summary>
[TestClass]
public class ArmStatesTest
{
    private readonly Dictionary<string, bool> animator = [];

    /// <returns>What the watch sends, as "TYPE STATE".</returns>
    private List<string> Sample(ArmStateWatch watch, float time)
    {
        return watch.Update(parameter => animator.TryGetValue(parameter, out bool value) && value, time).Select(c => $"{c.Key} {c.Value}").ToList();
    }

    [TestMethod]
    public void EveryState_IsSentOnceAtTheStart_ThenOnlyChanges()
    {
        ArmStateWatch watch = new();
        animator["holding_tool"] = true;

        List<string> first = Sample(watch, 0f);

        first.Should().Equal(ArmStates.Parameters.Select(p => p.Key == AnimChangeType.HOLDING_TOOL ? "HOLDING_TOOL ON" : $"{p.Key} OFF"),
                             "a late joiner needs every state");
        Sample(watch, 0.02f).Should().BeEmpty("nothing changed");

        animator["using_pda"] = true;
        Sample(watch, 0.04f).Should().Equal("USING_PDA ON");
        Sample(watch, 0.06f).Should().BeEmpty("on is sent once, not every frame");
    }

    [TestMethod]
    public void OneFrameClick_IsSentAsOnThenOffAfterTheDelay()
    {
        ArmStateWatch watch = new();
        Sample(watch, 0f);

        animator["using_tool"] = true;
        Sample(watch, 1f).Should().Equal("USING_TOOL ON");
        animator["using_tool"] = false;
        Sample(watch, 1.01f).Should().BeEmpty("the off waits, so the partner's animator sees the on for some frames");
        Sample(watch, 1f + ArmStateWatch.OFF_DELAY_SECONDS - 0.01f).Should().BeEmpty();
        Sample(watch, 1.011f + ArmStateWatch.OFF_DELAY_SECONDS).Should().Equal("USING_TOOL OFF");
        Sample(watch, 2f).Should().BeEmpty();
    }

    [TestMethod]
    public void FlickeringState_StaysOnUntilItIsOffForTheWholeDelay()
    {
        ArmStateWatch watch = new();
        Sample(watch, 0f);
        List<string> sent = [];

        // A welder on and off every other frame for a second, at 100 fps
        for (int frame = 0; frame < 100; frame++)
        {
            animator["using_tool"] = frame % 2 == 0;
            sent.AddRange(Sample(watch, 1f + frame * 0.01f));
        }
        animator["using_tool"] = false;
        for (int frame = 100; frame < 130; frame++)
        {
            sent.AddRange(Sample(watch, 1f + frame * 0.01f));
        }

        sent.Should().Equal("USING_TOOL ON", "USING_TOOL OFF");
    }

    [TestMethod]
    public void EverySyncedTypeHasItsGameParameter()
    {
        ArmStates.TryGetParameter(AnimChangeType.JUMP, out string jump).Should().BeTrue();
        jump.Should().Be("jump");
        ArmStates.TryGetParameter(AnimChangeType.HOLDING_TOOL, out string holding).Should().BeTrue();
        holding.Should().Be("holding_tool");
        ArmStates.TryGetParameter(AnimChangeType.UNDERWATER, out _).Should().BeFalse("the swimming pose has its own path, with the collider");
        ArmStates.Parameters.Select(p => p.Key).Should().OnlyHaveUniqueItems();
        ArmStates.Parameters.Select(p => p.Value).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void ServerReplaysStates_NotTheRevealOrTheFall()
    {
        AnimChangeType[] replayed = Enum.GetValues(typeof(AnimChangeType)).Cast<AnimChangeType>().Where(t => t.IsReplayedToLateJoiners()).ToArray();

        replayed.Should().BeEquivalentTo([
            AnimChangeType.UNDERWATER, AnimChangeType.USING_TOOL, AnimChangeType.USING_TOOL_ALT, AnimChangeType.USING_BUILDER,
            AnimChangeType.USING_PDA, AnimChangeType.ON_SURFACE, AnimChangeType.HOLDING_TOOL
        ]);
    }
}
