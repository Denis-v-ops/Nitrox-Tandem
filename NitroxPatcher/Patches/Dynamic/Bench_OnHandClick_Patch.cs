using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours.Gui.HUD;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Bench_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Bench t) => t.OnHandClick(default(GUIHand)));
    private static bool skipPrefix;

    public static bool Prefix(Bench __instance, GUIHand hand)
    {
        if (skipPrefix)
        {
            return true;
        }

        if (!__instance.TryGetIdOrWarn(out NitroxId id))
        {
            return true;
        }

        if (Resolve<SimulationOwnership>().HasExclusiveLock(id))
        {
            Log.Debug($"Already have an exclusive lock on the bench/chair: {id}");
            return true;
        }

        HandInteraction<Bench> context = new(__instance, hand);
        LockRequest<HandInteraction<Bench>> lockRequest = new(id, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, context);

        Resolve<SimulationOwnership>().RequestSimulationLock(lockRequest);

        return false;
    }

    private static void ReceivedSimulationLockResponse(NitroxId id, bool lockAcquired, HandInteraction<Bench> context)
    {
        Bench bench = context.Target;

        if (lockAcquired)
        {
            skipPrefix = true;
            bench.OnHandClick(context.GuiHand);
            skipPrefix = false;
            // The game can still refuse (in water, no room on either side): then the partner must not see a sit, which since
            // R1b also holds their copy of this player in place, and the seat goes back to the others
            if (bench.cinematicController && bench.cinematicController.cinematicModeActive)
            {
                Resolve<LocalPlayer>().BroadcastBenchChanged(id, BenchChanged.BenchChangeState.SITTING_DOWN);
            }
            else
            {
                Resolve<SimulationOwnership>().RequestSimulationLock(id, SimulationLockType.TRANSIENT);
            }
        }
        else
        {
            bench.gameObject.AddComponent<DenyOwnershipHand>();
            bench.isValidHandTarget = false;
        }
    }
}
