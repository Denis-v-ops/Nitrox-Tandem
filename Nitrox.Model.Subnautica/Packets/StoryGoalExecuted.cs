using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

[Serializable]
public class StoryGoalExecuted : Packet
{
    public string Key { get; }
    public EventType Type { get; }
    public float? Timestamp { get; set; }

    /// <summary>
    ///     A biome or location PDA line (COOP_FEATURES_SPEC D5) that the sender's own game executed, so the sender heard it.
    ///     The server adds it to the sender's heard goals. Set by the game, which knows the biome and location goals.
    /// </summary>
    public bool Experiential { get; }

    /// <summary>
    ///     A PDA found in the world (<c>StoryHandTarget</c>, COOP_FEATURES_SPEC D5): the partner's game adds the log entry
    ///     without the voice line.
    /// </summary>
    public bool Found { get; }

    public StoryGoalExecuted(string key, EventType type, float? timestamp = null, bool experiential = false, bool found = false)
    {
        Key = key;
        Type = type;
        Timestamp = timestamp;
        Experiential = experiential;
        Found = found;
    }

    public override string ToString()
    {
        return $"[StoryGoalExecuted Key: {Key}, Type: {Type}, Timestamp: {Timestamp}, Experiential: {Experiential}, Found: {Found}]";
    }

    public enum EventType
    {
        PDA,
        RADIO,
        ENCYCLOPEDIA,
        STORY
    }
}
