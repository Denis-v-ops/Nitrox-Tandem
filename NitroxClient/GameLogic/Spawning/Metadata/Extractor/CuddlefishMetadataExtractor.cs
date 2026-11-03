using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class CuddlefishMetadataExtractor : EntityMetadataExtractor<CuteFish, CuddlefishMetadata>
{
    public override CuddlefishMetadata Extract(CuteFish entity)
    {
        WaterParkCreatureMetadata waterParkCreature = entity.waterParkCreature ? WaterParkCreatureMetadataExtractor.ExtractValues(entity.waterParkCreature) : null;
        return new(entity._followingPlayer, entity._goodbyePlayed, waterParkCreature);
    }
}
