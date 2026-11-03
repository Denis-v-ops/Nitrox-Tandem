using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class IncubatorMetadataProcessor : EntityMetadataProcessor<IncubatorMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, IncubatorMetadata metadata)
    {
        // The hatch sends (powered, hatched) after the power-up sent (powered, not hatched), and an entity can be sent again
        // to a game that has it: powering up again plays its sound and goal again, and hatching again (Incubator.OnHatched)
        // starts every egg's hatch again, which makes new babies (MULTINAUGHTICA_SPEC M2).
        if (metadata.Powered)
        {
            IncubatorActivationTerminal terminal = gameObject.GetComponentInChildren<IncubatorActivationTerminal>();
            if (terminal && terminal.incubator && !terminal.incubator.powered)
            {
                terminal.incubator.SetPowered(true);
                terminal.onUseGoal?.Trigger();
                terminal.CloseDeck();
            }
        }

        if (metadata.Hatched)
        {
            Incubator incubator = gameObject.GetComponentInChildren<Incubator>();
            if (incubator && !incubator.hatched)
            {
                incubator.hatched = true;
                incubator.OnHatched();
            }
        }
    }
}
