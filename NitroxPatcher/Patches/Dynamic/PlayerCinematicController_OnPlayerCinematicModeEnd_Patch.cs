using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.CinematicController;
using NitroxClient.Unity.Helper;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Helper;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class PlayerCinematicController_OnPlayerCinematicModeEnd_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo targetMethod = Reflect.Method((PlayerCinematicController t) => t.OnPlayerCinematicModeEnd());

    public static void Prefix(PlayerCinematicController __instance)
    {
        if (!__instance.cinematicModeActive)
        {
            return;
        }

        if (!__instance.TryGetComponentInParent(out NitroxEntity entity, true))
        {
            Log.Warn($"[{nameof(PlayerCinematicController_OnPlayerCinematicModeEnd_Patch)}] - No NitroxEntity for \"{__instance.gameObject.GetFullHierarchyPath()}\" found!");
            return;
        }

        int identifier = __instance.gameObject.GetHierarchyPath(entity.gameObject).GetHashCode();
        
        Resolve<PlayerCinematics>().EndCinematicMode(Resolve<LocalPlayer>().SessionId.Value, entity.Id, identifier, __instance.playerViewAnimationName);
        // Give back only a lock the cinematic patches took (CinematicLocks): the same id can be the bench's, the helm's or a
        // vehicle's lock, which their own code gives back. A bed side stays taken while its player lies there.
        NitroxId lockId = BedLockId.Resolve(entity, __instance);
        if (!CinematicLocks.IsBedLieDown(__instance) && CinematicLocks.Release(lockId))
        {
            Resolve<SimulationOwnership>().RequestSimulationLock(lockId, SimulationLockType.TRANSIENT);
        }
    }
}
