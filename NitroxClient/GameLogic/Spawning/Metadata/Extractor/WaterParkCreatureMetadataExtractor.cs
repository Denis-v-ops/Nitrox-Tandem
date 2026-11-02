using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class WaterParkCreatureMetadataExtractor : EntityMetadataExtractor<WaterParkCreature, WaterParkCreatureMetadata>
{
    public override WaterParkCreatureMetadata Extract(WaterParkCreature entity)
    {
        // A cuddlefish's water park state travels in its CuddlefishMetadata (an entity holds one metadata)
        if (entity.GetComponent<CuteFish>())
        {
            return null;
        }
        return ExtractValues(entity);
    }

    public static WaterParkCreatureMetadata ExtractValues(WaterParkCreature entity)
    {
        // We don't need to save metadata for fishes with default values
        if (entity.age == -1)
        {
            return null;
        }
        return new(entity.age, entity.matureTime, entity.timeNextBreed, entity.bornInside);
    }
}
