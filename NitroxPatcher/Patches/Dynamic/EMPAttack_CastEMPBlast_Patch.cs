using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     M4 (FIX_SPEC S15a): a Crabsquid's EMP blast only touched what is in the game that runs the attack (the creature's
///     actions only run where it is simulated), so the partner's vehicle, tool and Cyclops were never disabled in the
///     partner's game. The simulating game announces the blast; the others cast it at their copy
///     (<see cref="NitroxClient.Communication.Packets.Processors.EmpFiredProcessor" />).
/// </summary>
public sealed partial class EMPAttack_CastEMPBlast_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((EMPAttack t) => t.CastEMPBlast());

    public static void Postfix(EMPAttack __instance)
    {
        Creature creature = __instance.GetComponentInParent<Creature>();
        if (creature && creature.TryGetNitroxId(out NitroxId creatureId) && Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            Resolve<IPacketSender>().Send(new EmpFired(creatureId));
        }
    }
}
