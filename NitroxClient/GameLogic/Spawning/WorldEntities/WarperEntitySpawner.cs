using System.Collections;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

/// <summary>
///     COOP_FEATURES_SPEC B1: a warper that another game's <see cref="WarperSpawner" /> spawned (WarperSpawner_Update_Patch).
///     When it arrives next to a spawner of ours that has no warper, it becomes that spawner's warper, as it is in the
///     spawning game: the spawner then doesn't spawn a second one if this game takes it over, its warp-out restarts the
///     spawner's timer, and the warp-in effect plays. Any other warper spawns as usual.
/// </summary>
public class WarperEntitySpawner(DefaultWorldEntitySpawner defaultWorldEntitySpawner) : IWorldEntitySpawner, IWorldEntitySyncSpawner
{
    /// <summary>
    ///     <see cref="WarperSpawner.GetSpawnPosition" /> places a new warper at most 3 m from its spawner.
    /// </summary>
    private const float MAX_SPAWNER_DISTANCE = 4f;

    private readonly DefaultWorldEntitySpawner defaultWorldEntitySpawner = defaultWorldEntitySpawner;

    public IEnumerator SpawnAsync(WorldEntity entity, Optional<GameObject> parent, EntityCell cellRoot, TaskResult<Optional<GameObject>> result)
    {
        yield return defaultWorldEntitySpawner.SpawnAsync(entity, parent, cellRoot, result);
        if (result.value.HasValue)
        {
            LinkToSpawner(result.value.Value);
        }
    }

    public bool SpawnsOwnChildren() => false;

    public bool SpawnSync(WorldEntity entity, Optional<GameObject> parent, EntityCell cellRoot, TaskResult<Optional<GameObject>> result)
    {
        if (!defaultWorldEntitySpawner.SpawnSync(entity, parent, cellRoot, result))
        {
            return false;
        }
        if (result.value.HasValue)
        {
            LinkToSpawner(result.value.Value);
        }
        return true;
    }

    private static void LinkToSpawner(GameObject gameObject)
    {
        if (!gameObject.TryGetComponent(out Warper warper))
        {
            return;
        }
        Vector3 position = warper.transform.position;
        foreach (WarperSpawner spawner in Object.FindObjectsOfType<WarperSpawner>())
        {
            if (!spawner.warper && (spawner.transform.position - position).sqrMagnitude <= MAX_SPAWNER_DISTANCE * MAX_SPAWNER_DISTANCE)
            {
                spawner.warper = warper;
                warper.WarpIn(spawner);
                return;
            }
        }
    }
}
