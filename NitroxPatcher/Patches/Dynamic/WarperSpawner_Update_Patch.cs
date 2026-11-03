using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
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
        Resolve<Items>().Dropped(__instance.warper.gameObject, TechType.Warper);
    }
}
