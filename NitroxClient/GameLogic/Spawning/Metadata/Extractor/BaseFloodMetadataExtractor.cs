using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

/// <remarks>
///     Flood levels are sent by <see cref="BaseFlooding" /> (only the base's lock holder, throttled). This extractor is for
///     everything else that extracts a base's metadata.
/// </remarks>
public class BaseFloodMetadataExtractor : EntityMetadataExtractor<Base, BaseFloodMetadata>
{
    public override BaseFloodMetadata Extract(Base entity)
    {
        if (!entity.TryGetComponent(out BaseFloodSim floodSim) || !floodSim.IsInitialized() || floodSim.cellWaterLevel == null)
        {
            return null;
        }
        return new BaseFloodMetadata(new NitroxInt3(floodSim.shape.x, floodSim.shape.y, floodSim.shape.z), BaseFloodLevels.Snapshot(floodSim.cellWaterLevel));
    }
}
