using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class PrecursorTeleporterMetadataProcessor : EntityMetadataProcessor<PrecursorTeleporterMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, PrecursorTeleporterMetadata metadata)
    {
        Log.Debug($"Received precursor teleporter metadata change for {gameObject.name} with data of {metadata}");

        PrecursorTeleporter precursorTeleporter = gameObject.GetComponent<PrecursorTeleporter>();
        // A teleporter only ever opens: closing isn't something the game does (older games and saves sent false for every
        // loaded teleporter, which faded an open portal out), and opening an open one fades its portal in again (MULTINAUGHTICA_SPEC M2)
        if (precursorTeleporter && metadata.IsOpen && !precursorTeleporter.isOpen)
        {
            precursorTeleporter.ToggleDoor(true);
        }
    }
}
