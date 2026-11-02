using System;
using System.Collections.Generic;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The game changes a player's infection only through story goals (<c>StoryGoalCustomEventHandler.NotifyGoalComplete</c>):
///     <c>Infection_Progress1</c>-<c>4</c> raise an existing infection to 0.25/0.5/0.75/1 (<c>InfectedMixin.IncreaseInfectedAmount</c>
///     only raises a value above 0) and <c>Infection_Progress5</c>, the cure, removes it. Those goals run only on the games
///     that are online at that moment, so a player who was offline keeps a stale infection forever (FIX_SPEC S12).
/// </summary>
internal static class InfectionStory
{
    private const string CURE_GOAL = "Infection_Progress5";

    private static readonly (string Goal, float Amount)[] progressGoals =
    [
        ("Infection_Progress1", 0.25f),
        ("Infection_Progress2", 0.5f),
        ("Infection_Progress3", 0.75f),
        ("Infection_Progress4", 1f)
    ];

    /// <summary>
    ///     The infection a player should have given the story goals the world has completed, applying the game's own rules.
    /// </summary>
    public static float Reconcile(float infectionAmount, IEnumerable<string> completedGoals)
    {
        HashSet<string> goals = new(completedGoals, StringComparer.OrdinalIgnoreCase);
        if (goals.Contains(CURE_GOAL))
        {
            return 0f;
        }
        if (infectionAmount <= 0f)
        {
            return infectionAmount;
        }
        foreach ((string goal, float amount) in progressGoals)
        {
            if (goals.Contains(goal))
            {
                infectionAmount = Math.Max(infectionAmount, amount);
            }
        }
        return infectionAmount;
    }
}
