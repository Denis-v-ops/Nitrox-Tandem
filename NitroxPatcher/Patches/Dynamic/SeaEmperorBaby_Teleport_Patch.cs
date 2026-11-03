using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     FIX_SPEC S17: a Sea Emperor baby that reaches the aquarium's teleporter (<c>SeaEmperorTeleporterTrigger</c>) teleports
///     away, which destroys it without a packet. The game that simulates it tells the server and the other games, which play
///     the teleport too (EntityDestroyedProcessor). Without this the server kept the five babies, and every later load of
///     their cell brought them back.
/// </summary>
public sealed partial class SeaEmperorBaby_Teleport_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBaby t) => t.Teleport());

    public static void Prefix(SeaEmperorBaby __instance)
    {
        if (__instance.TryGetNitroxId(out NitroxId id) && Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(id));
        }
    }
}
