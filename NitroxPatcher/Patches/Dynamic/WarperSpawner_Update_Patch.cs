using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC B1: <see cref="WarperSpawner.Update" /> instantiates a warper in every game that has the spawner
///     loaded, so each player met different warpers. Only the game that simulates the spawner (it is on the server's
///     utility whitelist) spawns one now, and registers it with the server; the other games get it as an entity
///     (<c>WarperEntitySpawner</c>). A spawner without a NitroxId spawns nothing: its warper would exist in one game only.
/// </summary>
public sealed partial class WarperSpawner_Update_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((WarperSpawner t) => t.Update());

    public static bool Prefix(WarperSpawner __instance, out Warper __state)
    {
        __state = __instance.warper;
        return __instance.TryGetNitroxId(out NitroxId spawnerId) && Resolve<SimulationOwnership>().HasAnyLockType(spawnerId);
    }

    public static void Postfix(WarperSpawner __instance, Warper __state, bool __runOriginal)
    {
        if (!__runOriginal || __state || !__instance.warper)
        {
            return;
        }
        // Update just spawned this warper (Object.Instantiate + WarpIn)
        GameObject warperObject = __instance.warper.gameObject;
        NitroxId warperId = NitroxEntity.GenerateNewId(warperObject);
        int level = warperObject.TryGetComponent(out LargeWorldEntity largeWorldEntity) ? (int)largeWorldEntity.cellLevel : 0;
        Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(warperObject);
        WorldEntity warperEntity = new(warperObject.transform.ToWorldDto(), level, warperObject.GetComponent<UniqueIdentifier>().classId, false, warperId,
                                       TechType.Warper.ToDto(), metadata.OrNull(), null, []);

        Entities entities = Resolve<Entities>();
        // This game has it already: when the server sends it back (its cell becomes visible again), it must update this
        // warper, not spawn a second one with the same id (seen in game at the edge of the loaded cells)
        entities.MarkAsSpawned(warperEntity);
        entities.BroadcastEntitySpawnedByClient(warperEntity);
    }
}
