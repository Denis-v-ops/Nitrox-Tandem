using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class BaseFloodMetadataProcessor(BaseFlooding baseFlooding) : EntityMetadataProcessor<BaseFloodMetadata>
{
    private readonly BaseFlooding baseFlooding = baseFlooding;

    public override void ProcessMetadata(GameObject gameObject, BaseFloodMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out BaseFloodSim floodSim))
        {
            Log.Error($"[{nameof(BaseFloodMetadataProcessor)}] Could not find BaseFloodSim on {gameObject.name}");
            return;
        }
        baseFlooding.ApplyMetadata(floodSim, metadata);
    }
}
