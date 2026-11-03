using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

/// <summary>
///     The charge sent by the game holding the base's lock (<see cref="NitroxClient.GameLogic.Bases.BasePowerSync" />), or
///     the stored one when the module spawns. Replaces this game's own value.
/// </summary>
public class PowerSourceMetadataProcessor : EntityMetadataProcessor<PowerSourceMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, PowerSourceMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out PowerSource powerSource))
        {
            Log.Error($"[{nameof(PowerSourceMetadataProcessor)}] Could not find PowerSource component on {gameObject.name}");
            return;
        }

        powerSource.SetPower(metadata.Power);
    }
}
