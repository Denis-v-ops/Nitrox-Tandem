using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC B1: a warper warps itself away (<see cref="Warper.WarpOut" />, which destroys it 0.73 s later in
///     <c>EndWarpOut</c>) from its <c>WarpOut</c> or <c>WarperInspectPlayer</c> action, which only run where it is simulated,
///     or when its spawner is disabled. The game that simulates it tells the server and the other games when the warp-out
///     starts, so they play it at the same time (see EntityDestroyedProcessor). Sent at the end (EndWarpOut) instead, one
///     warper stayed about 5 s longer in one game than in the other (B1 scenario); likely the lock moved during the 0.73 s
///     animation (H).
/// </summary>
public sealed partial class Warper_WarpOut_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Warper t) => t.WarpOut());

    public static void Prefix(Warper __instance)
    {
        // WarperInspectPlayer calls WarpOut every frame while the player is close
        if (__instance.IsInvoking(nameof(Warper.EndWarpOut)))
        {
            return;
        }
        if (__instance.TryGetNitroxId(out NitroxId warperId) && Resolve<SimulationOwnership>().HasAnyLockType(warperId))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(warperId));
        }
    }
}
