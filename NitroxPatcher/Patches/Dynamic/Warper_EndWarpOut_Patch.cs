using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC B1: a warper warps itself away (<see cref="Warper.EndWarpOut" /> destroys it) after its
///     <c>WarpOut</c> action, which only runs where it is simulated. That game tells the server and the other games.
/// </summary>
public sealed partial class Warper_EndWarpOut_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Warper t) => t.EndWarpOut());

    public static void Prefix(Warper __instance)
    {
        if (__instance.TryGetNitroxId(out NitroxId warperId) && Resolve<SimulationOwnership>().HasAnyLockType(warperId))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(warperId));
        }
    }
}
