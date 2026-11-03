using System.Runtime.CompilerServices;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     Remembers, per sulfur plant, whether the home's saved state reached it, and since when its lock holder has been asking
///     to act (<see cref="CrashHomeRules.MayActOnItsNumber" />).
/// </summary>
/// <remarks>Ported from MultiNaughtica, adapted.</remarks>
public static class CrashHomeMetadataGate
{
    private sealed class State
    {
        public bool MetadataApplied;
        public float FirstAsked = -1f;
    }

    // Weak keys: an entry goes with its home
    private static readonly ConditionalWeakTable<CrashHome, State> states = new();

    /// <summary>
    ///     The home's saved state was applied (<c>CrashHomeMetadataProcessor</c>).
    /// </summary>
    public static void MarkMetadataApplied(CrashHome home)
    {
        states.GetOrCreateValue(home).MetadataApplied = true;
    }

    /// <summary>
    ///     Whether the home's lock holder may act on its number now. Call it only when the holder is about to act: the first
    ///     call starts the wait.
    /// </summary>
    public static bool MayActOnItsNumber(CrashHome home)
    {
        State state = states.GetOrCreateValue(home);
        float now = Time.realtimeSinceStartup;
        if (state.FirstAsked < 0f)
        {
            state.FirstAsked = now;
        }
        return CrashHomeRules.MayActOnItsNumber(state.MetadataApplied, now - state.FirstAsked);
    }
}
