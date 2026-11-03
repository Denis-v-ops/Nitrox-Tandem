using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor.Abstract;

namespace NitroxClient.GameLogic.Spawning.Metadata.Extractor;

public class EscapePodMetadataExtractor(LocalPlayer localPlayer) : EntityMetadataExtractor<EscapePod, EscapePodMetadata>
{
    public override EscapePodMetadata Extract(EscapePod entity)
    {
        Radio radio = entity.radioSpawner.spawnedObj.RequireComponent<Radio>();
        return new EscapePodMetadata(
            entity.liveMixin.IsFullHealth(),
            radio.liveMixin.IsFullHealth(),
            // Only the local player's own use: the server adds it to the others' (EntityMetadataUpdateProcessor)
            entity.bottomHatchUsed ? [localPlayer.PlayerName] : [],
            entity.topHatchUsed ? [localPlayer.PlayerName] : []
        );
    }
}
