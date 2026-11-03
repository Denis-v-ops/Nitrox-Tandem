using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Seamoth hitch-a-ride (COOP_FEATURES_SPEC T5): a player lets go of the Seamoth at <see cref="Position" />. The
///     hitcher's game sends it when its player lets go (the Exit key, going down, the Seamoth docking or gone, the pilot
///     out); <see cref="PlayerId" /> is ignored then and the server relays it to the others. The server sends it to everyone
///     when it ends a hitch itself: the pilot got out or left, the Seamoth docked or was destroyed.
/// </summary>
[Serializable]
public sealed class PlayerUnhitched : Packet
{
    public SessionId PlayerId { get; }

    public NitroxVector3 Position { get; }

    public PlayerUnhitched(SessionId playerId, NitroxVector3 position)
    {
        PlayerId = playerId;
        Position = position;
    }
}
