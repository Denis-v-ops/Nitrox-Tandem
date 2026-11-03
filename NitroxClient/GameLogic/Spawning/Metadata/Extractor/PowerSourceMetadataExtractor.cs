using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Bases;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class PowerSourceMetadataExtractor : EntityMetadataExtractor<PowerSource, PowerSourceMetadata>
{
    public override PowerSourceMetadata Extract(PowerSource powerSource)
    {
        // Only the modules whose power is synced (BasePowerSync); a thermal plant's power is in its ThermalPlantMetadata
        if (!BasePowerSync.IsSynced(powerSource) || powerSource.TryGetComponent(out ThermalPlant _))
        {
            return null;
        }
        return new(powerSource.power);
    }
}
