namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M7: how long the game's "Saving game" indicator shows a server save, and whether the game's
///     indicator or its notice shows it.
/// </summary>
[TestClass]
public class ServerSaveIndicatorTest
{
    private const int FRAME = 1000;

    [TestMethod]
    public void NothingShowsBeforeTheFirstSave()
    {
        ServerSaveIndicator indicator = new();

        indicator.IsShown(0f).Should().BeFalse();
        indicator.IsShown(500f).Should().BeFalse();
    }

    [TestMethod]
    public void AFastSaveStillShowsForTwoSecondsFromItsStart()
    {
        ServerSaveIndicator indicator = new();
        indicator.SaveStarted(1, 10f, FRAME, true);
        indicator.SaveEnded(1, 10.3f);

        indicator.IsShown(10.5f).Should().BeTrue();
        indicator.IsShown(11.9f).Should().BeTrue();
        indicator.IsShown(12f).Should().BeFalse();
    }

    [TestMethod]
    public void ASlowSaveShowsUntilItsEnd()
    {
        ServerSaveIndicator indicator = new();
        indicator.SaveStarted(1, 10f, FRAME, true);

        indicator.IsShown(14.9f).Should().BeTrue();
        indicator.SaveEnded(1, 15f);
        indicator.IsShown(15f).Should().BeFalse();
    }

    [TestMethod]
    public void ASaveWhoseEndNeverArrivesStopsShowingAfterSixtySeconds()
    {
        ServerSaveIndicator indicator = new();
        indicator.SaveStarted(1, 10f, FRAME, true);

        indicator.IsShown(69.9f).Should().BeTrue();
        indicator.IsShown(70f).Should().BeFalse();
    }

    [TestMethod]
    public void AnEndAfterTheTimeoutDoesNotShowItAgain()
    {
        ServerSaveIndicator indicator = new();
        indicator.SaveStarted(1, 10f, FRAME, true);
        indicator.SaveEnded(1, 80f);

        indicator.IsShown(80f).Should().BeFalse();
        indicator.IsShown(81f).Should().BeFalse();
    }

    [TestMethod]
    public void AnEndWithoutItsStartChangesNothing()
    {
        // A player who joins during a save gets only its end
        ServerSaveIndicator indicator = new();
        indicator.SaveEnded(3, 10f);
        indicator.IsShown(10f).Should().BeFalse();

        // ...and the end of an older save doesn't hide the one that runs
        indicator.SaveStarted(4, 20f, FRAME, true);
        indicator.SaveEnded(3, 20.5f);
        indicator.IsShown(30f).Should().BeTrue();
        indicator.SaveEnded(4, 31f);
        indicator.IsShown(31f).Should().BeFalse();
    }

    [TestMethod]
    public void ANewSaveStartsItsOwnTwoSeconds()
    {
        ServerSaveIndicator indicator = new();
        indicator.SaveStarted(1, 10f, FRAME, true);
        indicator.SaveEnded(1, 10.2f);
        indicator.SaveStarted(2, 11.5f, FRAME, true);
        indicator.SaveEnded(2, 11.6f);

        indicator.IsShown(13.4f).Should().BeTrue();
        indicator.IsShown(13.5f).Should().BeFalse();
    }

    [TestMethod]
    public void ASaveThatStartsBeforeTheLastOneEndedReplacesIt()
    {
        ServerSaveIndicator indicator = new();
        indicator.SaveStarted(1, 10f, FRAME, true);
        indicator.SaveStarted(2, 30f, FRAME, true);

        indicator.IsShown(85f).Should().BeTrue("the second save times out 60 s after its own start");
        indicator.SaveEnded(1, 31f);
        indicator.IsShown(35f).Should().BeTrue("the first save's end belongs to the save that was replaced");
        indicator.SaveEnded(2, 40f);
        indicator.IsShown(40f).Should().BeFalse();
    }

    [TestMethod]
    public void TheGamesIndicatorShowsTheSave_WhenItsUpdateRanInTheLastFrames()
    {
        ServerSaveIndicator indicator = new();
        indicator.OnIndicatorUpdate(FRAME - 1, 9.9f).Should().BeFalse("no save runs yet");

        indicator.SaveStarted(1, 10f, FRAME, true).Should().Be(ServerSaveIndicator.Display.INDICATOR);
        indicator.OnIndicatorUpdate(FRAME, 10f).Should().BeTrue();
        indicator.OnIndicatorUpdate(FRAME + 1, 10.01f).Should().BeTrue();
    }

    [TestMethod]
    public void WithoutARunningIndicator_TheNoticeShowsTheSave_OnlyInTheWorld()
    {
        ServerSaveIndicator never = new();
        never.SaveStarted(1, 10f, FRAME, true).Should().Be(ServerSaveIndicator.Display.NOTICE);
        never.SaveStarted(2, 20f, FRAME, false).Should().Be(ServerSaveIndicator.Display.NOTHING, "the player still loads");

        // An indicator that ran long ago (in the menu) doesn't run any more
        ServerSaveIndicator stopped = new();
        stopped.OnIndicatorUpdate(5, 1f);
        stopped.SaveStarted(1, 10f, FRAME, true).Should().Be(ServerSaveIndicator.Display.NOTICE);
    }
}
