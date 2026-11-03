using System;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M7: when the game's own "Saving game" indicator shows a save of the server
///     (<see cref="ServerSaving" />). Nitrox games never save locally, so the game shows its indicator only for its own
///     saves (<c>SaveLoadManager.isSaving</c>), which never happen. No Unity calls here: the times and frames come from the
///     caller, so the rules can be tested without the game.
/// </summary>
public sealed class ServerSaveIndicator
{
    /// <summary>
    ///     A save that ends sooner still shows this long from its start, so the text can be read.
    /// </summary>
    public const float MIN_SHOWN_SECONDS = 2f;

    /// <summary>
    ///     A save whose end never arrives stops showing this long after its start.
    /// </summary>
    public const float TIMEOUT_SECONDS = 60f;

    /// <summary>
    ///     The game's indicator counts as running when its <c>Update</c> ran within this many frames.
    /// </summary>
    private const int INDICATOR_RUNNING_FRAMES = 2;

    private float hiddenFrom = float.NegativeInfinity;
    private int lastIndicatorFrame = -1;
    private int? runningSave;
    private float startedAt;

    public enum Display
    {
        /// <summary>The player is still loading: nothing to show it on yet.</summary>
        NOTHING,

        /// <summary>The game's <c>SavingIndicator</c> runs; its <c>Update</c> shows the save.</summary>
        INDICATOR,

        /// <summary>No <c>SavingIndicator</c> runs in the play scene: the game's notice with the same text, once.</summary>
        NOTICE
    }

    /// <param name="now">Unscaled time in seconds (the pause menu freezes the scaled time).</param>
    /// <param name="frame">The current frame number.</param>
    /// <param name="inGame">The player is in the world (the initial sync has completed).</param>
    /// <returns>How the caller shows this save.</returns>
    public Display SaveStarted(int number, float now, int frame, bool inGame)
    {
        runningSave = number;
        startedAt = now;
        hiddenFrom = now + TIMEOUT_SECONDS;
        if (IsIndicatorRunning(frame))
        {
            return Display.INDICATOR;
        }
        return inGame ? Display.NOTICE : Display.NOTHING;
    }

    /// <summary>
    ///     The save finished or failed. An end without its start (the player joined during the save) changes nothing.
    /// </summary>
    public void SaveEnded(int number, float now)
    {
        if (runningSave != number)
        {
            return;
        }
        runningSave = null;
        hiddenFrom = Math.Max(now, startedAt + MIN_SHOWN_SECONDS);
    }

    public bool IsShown(float now) => now < hiddenFrom;

    /// <summary>
    ///     Called from every <c>SavingIndicator.Update</c>.
    /// </summary>
    /// <returns>Whether the indicator shows now.</returns>
    public bool OnIndicatorUpdate(int frame, float now)
    {
        lastIndicatorFrame = frame;
        return IsShown(now);
    }

    private bool IsIndicatorRunning(int frame) => lastIndicatorFrame >= 0 && frame - lastIndicatorFrame <= INDICATOR_RUNNING_FRAMES;
}
