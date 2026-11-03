using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Syncs metadata when a fish changes its breeding state if the local player is simulating the said fish.
/// </summary>
public sealed partial class WaterParkCreature_ResetBreedTime_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((WaterParkCreature t) => t.ResetBreedTime());

    public static void Postfix(WaterParkCreature __instance)
    {
        if (!__instance.TryGetNitroxId(out NitroxId creatureId) ||
            !Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            return;
        }

        // A cuddlefish's water park state is part of its CuddlefishMetadata
        EntityMetadata metadata = __instance.TryGetComponent(out CuteFish cuteFish) ?
                                      Resolve<CuddlefishMetadataExtractor>().Extract(cuteFish) :
                                      Resolve<WaterParkCreatureMetadataExtractor>().Extract(__instance);
        if (metadata != null)
        {
            Resolve<Entities>().BroadcastMetadataUpdate(creatureId, metadata);
        }
    }
}
