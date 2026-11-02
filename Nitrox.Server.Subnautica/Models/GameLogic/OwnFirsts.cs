using System.Collections.Generic;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC D5b "your own firsts": each player hears the PDA's biome and location lines from their own visit,
///     also after a rejoin.
/// </summary>
/// <remarks>
///     The server doesn't know which goals are biome or location lines; the game does (<c>ExperientialGoals</c>). It flags
///     its own executes of them (<see cref="StoryGoalExecuted.Experiential" />), and the server keeps them per player
///     (<see cref="Player.HeardGoals" />, saved). Every such line in the shared PDA log got there from someone's own
///     execute, so the union of all players' heard goals holds every biome or location line in the log, and no world-level
///     list is needed. Log entries from saves made before D5b are in nobody's heard set and stay shared as before.
/// </remarks>
internal static class OwnFirsts
{
    /// <summary>
    ///     The biome and location lines another player heard and the joining player hasn't. The initial sync leaves them out
    ///     of the joining player's PDA log, so the line plays when the joining player gets there.
    /// </summary>
    public static HashSet<string> UnheardBy(ISet<string> joinerHeard, IEnumerable<ISet<string>> everyonesHeard, bool partnerDiscoveriesSilent)
    {
        HashSet<string> result = [];
        if (!partnerDiscoveriesSilent)
        {
            return result;
        }
        foreach (ISet<string> heard in everyonesHeard)
        {
            foreach (string key in heard)
            {
                if (!joinerHeard.Contains(key))
                {
                    result.Add(key);
                }
            }
        }
        return result;
    }
}
