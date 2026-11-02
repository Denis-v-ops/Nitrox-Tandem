using System;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets
{
    [Serializable]
    public class PlayerTeleported : Packet
    {
        public string PlayerName { get; }
        public NitroxVector3 DestinationFrom { get; }
        public NitroxVector3 DestinationTo { get; }
        public Optional<NitroxId> SubRootID { get; }
        /// <summary>
        ///     The game's Player.precursorOutOfWater at the destination: dry precursor rooms only get it from trigger volumes, which a teleport skips.
        /// </summary>
        public bool InPrecursor { get; }

        public PlayerTeleported(string playerName, NitroxVector3 destinationFrom, NitroxVector3 destinationTo, Optional<NitroxId> subRootID, bool inPrecursor)
        {
            PlayerName = playerName;
            DestinationFrom = destinationFrom;
            DestinationTo = destinationTo;
            SubRootID = subRootID;
            InPrecursor = inPrecursor;
        }
    }
}
