using System.Collections;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Spawning.Metadata.Processor;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

public class CrashEntitySpawner : IWorldEntitySpawner, IWorldEntitySyncSpawner
{
    public IEnumerator SpawnAsync(WorldEntity entity, Optional<GameObject> parent, EntityCell cellRoot, TaskResult<Optional<GameObject>> result)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, classId: entity.ClassId))
        {
            TaskResult<GameObject> prefabResult = new();
            yield return DefaultWorldEntitySpawner.RequestPrefab(entity.ClassId, prefabResult);
            if (!prefabResult.Get())
            {
                Log.Error($"Couldn't find a prefab for {nameof(WorldEntity)} of ClassId {entity.ClassId}");
                yield break;
            }
            prefab = prefabResult.Get();
        }

        GameObject gameObject = GameObjectExtensions.InstantiateWithId(prefab, entity.Id);
        if (!VerifyCanSpawnOrError(entity, gameObject, parent.Value, out Crash crash, out CrashHome crashHome))
        {
            yield break;
        }
        SetupObject(entity, crash, crashHome);

        result.Set(gameObject);
    }

    public bool SpawnsOwnChildren() => false;

    public bool SpawnSync(WorldEntity entity, Optional<GameObject> parent, EntityCell cellRoot, TaskResult<Optional<GameObject>> result)
    {
        if (!DefaultWorldEntitySpawner.TryGetCachedPrefab(out GameObject prefab, classId: entity.ClassId))
        {
            return false;
        }

        GameObject gameObject = GameObjectExtensions.InstantiateWithId(prefab, entity.Id);
        if (!VerifyCanSpawnOrError(entity, gameObject, parent.Value, out Crash crash, out CrashHome crashHome))
        {
            return true;
        }
        SetupObject(entity, crash, crashHome);

        result.Set(gameObject);
        return true;
    }

    private static bool VerifyCanSpawnOrError(WorldEntity entity, GameObject prefabObject, GameObject parentObject, out Crash crash, out CrashHome crashHome)
    {
        if (!prefabObject.TryGetComponent(out crash))
        {
            Log.Error($"Couldn't find component {nameof(Crash)} on prefab with ClassId: {entity.ClassId}");
            crashHome = null;
            // M4: the object exists already and nothing else knows it; left alone it would swim at the prefab's position
            Object.Destroy(prefabObject);
            return false;
        }
        if (parentObject && parentObject.TryGetComponent(out crashHome))
        {
            return true;
        }

        crashHome = null;
        Log.Error($"Couldn't find a valid parent for {entity}");
        Object.Destroy(prefabObject);
        return false;
    }

    private static void SetupObject(WorldEntity worldEntity, Crash crash, CrashHome crashHome)
    {
        crash.transform.SetPositionAndRotation(worldEntity.Transform.Position.ToUnity(), worldEntity.Transform.Rotation.ToUnity());
        crash.transform.localScale = worldEntity.Transform.LocalScale.ToUnity();

        // M4: a home points to one fish, and only CrashHome.OnDestroy removes a spawned fish (it isn't a child of the home's
        // transform). A second fish object for the same home would stay in the world for good (CrashHomeRules.DecideIncomingFish).
        Crash previousFish = crashHome.crash;
        if (CrashHomeRules.DecideIncomingFish(previousFish, previousFish == crash) == CrashHomeRules.IncomingFishCall.ReplaceTheOldFish)
        {
            Log.Warn($"[{nameof(CrashEntitySpawner)}] The CrashHome of {worldEntity.Id} had another living fish, it is replaced");
            Object.Destroy(previousFish.gameObject);
        }

        crashHome.crash = crash;
        CrashHomeMetadataProcessor.UpdateCrashHomeOpen(crashHome);
        LargeWorldStreamer.main.MakeEntityTransient(crash.gameObject);
    }
}
