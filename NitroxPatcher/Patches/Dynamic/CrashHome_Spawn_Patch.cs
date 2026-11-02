using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class CrashHome_Spawn_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((CrashHome t) => t.Spawn());

    public static bool Prefix(CrashHome __instance)
    {
        bool holdsLock = __instance.TryGetNitroxId(out NitroxId crashHomeId) && Resolve<SimulationOwnership>().HasAnyLockType(crashHomeId);

        // M4: as in CrashHome_Update_Patch, a home that hasn't got its saved state yet has the prefab's number
        if (holdsLock && !CrashHomeMetadataGate.MayActOnItsNumber(__instance))
        {
            return false;
        }

        switch (CrashHomeRules.DecideSpawn(holdsLock, __instance.crash))
        {
            case CrashHomeRules.SpawnCall.MakeFish:
                return true;
            case CrashHomeRules.SpawnCall.FishAlready:
                // The number says "make one" although the home has its fish (a timer started before the server's fish
                // arrived, or saved next to it): a second fish would stay beside the first. Set the number right and send it.
                __instance.spawnTime = CrashHomeRules.FISH_ALIVE;
                Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(__instance);
                if (metadata.HasValue)
                {
                    Resolve<Entities>().BroadcastMetadataUpdate(crashHomeId, metadata.Value);
                }
                return false;
            default:
                return false;
        }
    }

    /*
     * this.spawnTime = -1f;
     * BroadcastFishCreated(gameObject);            [INSERTED LINE]
     * if (LargeWorldStreamer.main != null)
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Stfld, Reflect.Field((CrashHome t) => t.spawnTime)))
                                            .Advance(1)
                                            .InsertAndAdvance(new CodeInstruction(OpCodes.Ldloc_0))
                                            .InsertAndAdvance(new CodeInstruction(OpCodes.Call, Reflect.Method(() => BroadcastFishCreated(default))))
                                            .InstructionEnumeration();
    }

    public static void BroadcastFishCreated(GameObject crashFishObject)
    {
        if (!crashFishObject.TryGetComponentInParent(out CrashHome crashHome, true) ||
            !crashHome.TryGetNitroxId(out NitroxId crashHomeId) || !DayNightCycle.main)
        {
            return;
        }
        NitroxId crashFishId = NitroxEntity.GenerateNewId(crashFishObject);
        LargeWorldEntity largeWorldEntity = crashFishObject.GetComponent<LargeWorldEntity>();
        UniqueIdentifier uniqueIdentifier = crashFishObject.GetComponent<UniqueIdentifier>();

        // Broadcast the new CrashHome's metadata (spawnTime = -1)
        Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(crashHome);
        if (metadata.HasValue)
        {
            Resolve<Entities>().BroadcastMetadataUpdate(crashHomeId, metadata.Value);
        }

        // Create the entity
        WorldEntity crashFishEntity = new(crashFishObject.transform.ToWorldDto(), (int)largeWorldEntity.cellLevel, uniqueIdentifier.classId, false, crashFishId, TechType.Crash.ToDto(), null, crashHomeId, new List<Entity>());
        Resolve<Entities>().BroadcastEntitySpawnedByClient(crashFishEntity);
    }
}
