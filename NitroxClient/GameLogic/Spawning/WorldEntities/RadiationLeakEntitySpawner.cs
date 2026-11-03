using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Abstract;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

public class RadiationLeakEntitySpawner : SyncEntitySpawner<RadiationLeakEntity>
{
    // This constant is defined by Subnautica and should never be modified (same as for SubnauticaWorldModifier)
    private const int TOTAL_LEAKS = 11;
    private readonly TimeManager timeManager;
    private readonly List<float> registeredLeaksFixTime = new();

    public RadiationLeakEntitySpawner(TimeManager timeManager)
    {
        this.timeManager = timeManager;
    }

    protected override IEnumerator SpawnAsync(RadiationLeakEntity entity, TaskResult<Optional<GameObject>> result)
    {
        SpawnSync(entity, result);
        yield break;
    }

    protected override bool SpawnSync(RadiationLeakEntity entity, TaskResult<Optional<GameObject>> result)
    {
        // This script is located under (Aurora Scene) //Aurora-Main/Aurora so it's a good starting point to search through the GameObjects
        CrashedShipExploder crashedShipExploder = CrashedShipExploder.main;
        LeakingRadiation leakingRadiation = LeakingRadiation.main;
        if (!crashedShipExploder || !leakingRadiation || entity.Metadata is not RadiationMetadata metadata)
        {
            return true;
        }
        Transform radiationLeaksHolder = crashedShipExploder.transform.Find("radiationleaks").GetChild(0);
        RadiationLeak radiationLeak = radiationLeaksHolder.GetChild(entity.ObjectIndex).GetComponent<RadiationLeak>();
        NitroxEntity.SetNewId(radiationLeak.gameObject, entity.Id);
        radiationLeak.liveMixin.health = metadata.Health;
        registeredLeaksFixTime.Add(metadata.FixRealTime);

        // We can only calculate the radiation increment and dissipation once we got all radiation leaks info
        if (crashedShipExploder.IsExploded() && registeredLeaksFixTime.Count == TOTAL_LEAKS)
        {
            RecalculateRadiationRadius(leakingRadiation);
        }

        return true;
    }

    public void RecalculateRadiationRadius(LeakingRadiation leakingRadiation)
    {
        // The maths is RadiationRadiusModel (unit-tested, MULTINAUGHTICA_SPEC M2): the radius grows from the explosion while
        // any leak leaks and shrinks once all are fixed
        RadiationRadiusModel.Result result = RadiationRadiusModel.Compute((float)timeManager.RealTimeElapsed, timeManager.AuroraRealExplosionTime, registeredLeaksFixTime, TOTAL_LEAKS,
                                                                          leakingRadiation.kStartRadius, leakingRadiation.kGrowRate, leakingRadiation.kNaturalDissipation,
                                                                          leakingRadiation.kMaxRadius, leakingRadiation.currentRadius);
        if (result.AllFixed)
        {
            leakingRadiation.radiationFixed = true;
        }
        if (result.ApplyRadius)
        {
            leakingRadiation.currentRadius = result.Radius;
            leakingRadiation.damagePlayerInRadius.damageRadius = result.Radius;
            leakingRadiation.radiatePlayerInRange.radiateRadius = result.Radius;
        }
    }

    protected override bool SpawnsOwnChildren(RadiationLeakEntity entity) => false;
}
