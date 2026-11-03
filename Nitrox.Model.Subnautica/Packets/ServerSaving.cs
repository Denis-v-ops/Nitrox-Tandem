using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The server saves the world (the timed autosave, the <c>save</c> command, the save when the last player leaves): sent
///     to every connected player when a save starts and again when it ends. The games show the game's own "Saving game"
///     indicator meanwhile. Sent by the server only; a copy sent by a game is dropped (DefaultServerPacketProcessor).
/// </summary>
[Serializable]
public sealed class ServerSaving : Packet
{
    /// <summary>
    ///     Which save of this server run (1 for the first): the end of a save carries the number of its start.
    /// </summary>
    public int Number { get; }

    public SavePhase Phase { get; }

    public ServerSaving(int number, SavePhase phase)
    {
        Number = number;
        Phase = phase;
    }

    public enum SavePhase
    {
        STARTED,
        FINISHED,
        FAILED
    }
}
