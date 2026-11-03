using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class CuddlefishMetadataProcessor : EntityMetadataProcessor<CuddlefishMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, CuddlefishMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out CuteFish cuteFish))
        {
            Log.Error($"[{nameof(CuddlefishMetadataProcessor)}] Could not find {nameof(CuteFish)} on {gameObject.name}");
            return;
        }

        if (metadata.WaterParkCreature != null)
        {
            Resolve<WaterParkCreatureMetadataProcessor>().ProcessMetadata(gameObject, metadata.WaterParkCreature);
        }

        using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
        {
            // The farewell first: its setter makes the cuddlefish stay. The fields are set directly so the patched setter doesn't
            // broadcast it again; the followingPlayer setter applies the follow/stay state (components, cell level) as CuteFish.Start does.
            // In a tank only the field: the setter would take the creature out of the tank's hierarchy.
            cuteFish._goodbyePlayed = metadata.GoodbyePlayed;
            bool following = metadata.FollowingPlayer && !metadata.GoodbyePlayed;
            if (cuteFish.waterParkCreature.IsInsideWaterPark())
            {
                cuteFish._followingPlayer = following;
            }
            else if (cuteFish._followingPlayer != following || cuteFish.creatureFollowPlayer.enabled != following)
            {
                cuteFish.followingPlayer = following;
            }
        }
    }
}
