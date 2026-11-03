using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic.InitialSync;

/// <summary>
///     Holds story packets that arrive before this game's initial sync is applied, and applies them right after it, in order
///     (MULTINAUGHTICA_SPEC M2). The server broadcasts to every connected session (<c>LiteNetLibServerService</c>), and the game
///     processes packets from the main menu on (<c>Multiplayer</c> exists from the connection), so a partner's story goal or
///     Aurora update reaches a game that is in the menu, loading the world (<c>StoryGoalScheduler.main</c> missing: it threw)
///     or applying the initial sync, whose older snapshot then overwrote it. One per session (lifetime scope).
/// </summary>
public sealed class InitialSyncDeferral
{
    private readonly Queue<Action> pending = new();

    public bool Synced { get; private set; }

    /// <returns>True if <paramref name="apply" /> was held for later: the initial sync isn't applied yet.</returns>
    public bool Defer(Action apply)
    {
        if (Synced)
        {
            return false;
        }
        pending.Enqueue(apply);
        return true;
    }

    /// <summary>The initial sync is applied: runs what was held, in the order it came. One that throws doesn't stop the others.</summary>
    public void MarkSynced()
    {
        Synced = true;
        while (pending.Count > 0)
        {
            Action apply = pending.Dequeue();
            try
            {
                apply();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{nameof(InitialSyncDeferral)}] A packet held until the initial sync failed");
            }
        }
    }
}
