using NitroxClient.Communication;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class RadiationMetadataProcessor : EntityMetadataProcessor<RadiationMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, RadiationMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out LiveMixin liveMixin))
        {
            Log.Error($"[{nameof(RadiationMetadataProcessor)}] Couldn't find LiveMixin on {gameObject}");
            return;
        }
        // A leak only gains health: an older update must not make a repaired leak leak again (MULTINAUGHTICA_SPEC M2)
        if (!RadiationMetadata.IsProgress(liveMixin.health, metadata.Health))
        {
            return;
        }

        using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
        {
            Resolve<LiveMixinManager>().SyncRemoteHealth(liveMixin, metadata.Health);
        }
    }
}
