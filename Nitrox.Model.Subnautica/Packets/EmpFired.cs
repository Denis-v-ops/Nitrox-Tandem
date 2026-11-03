using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     A Crabsquid cast its EMP blast (MULTINAUGHTICA_SPEC M4, FIX_SPEC S15a). Only the game that simulates the creature
///     runs the attack, and its blast only touches that game's objects (vehicles, held tool, Cyclops). The other games that
///     see the creature cast the same blast at their copy of it.
/// </summary>
[Serializable]
public sealed class EmpFired : Packet
{
    public NitroxId CreatureId { get; }

    public EmpFired(NitroxId creatureId)
    {
        CreatureId = creatureId;
    }
}
