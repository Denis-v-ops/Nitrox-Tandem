using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Syncs egg deletion when hatching, and keeps a ripe egg on the games that don't simulate its containment tank.
/// </summary>
/// <remarks>
/// Only the game holding the tank's simulation lock hatches: it sends the egg's <see cref="EntityDestroyed"/> and is the
/// only one allowed to spawn the creature (<see cref="WaterParkCreature_BornAsync_Patch.CanBeBorn"/>). But every game runs
/// the egg's timer and the game destroys a ripe egg unconditionally (<c>CreatureEgg.Hatch</c>). When another game got
/// there first, its <c>liveMixin.Kill()</c> made <see cref="LiveMixin_Kill_Patch"/> broadcast <see cref="EntityDestroyed"/>
/// (eggs are <c>destroyOnDeath</c>), so the egg vanished from the server and from the holder's game before it hatched
/// there: no creature anywhere (#2538). The other games now keep the ripe egg until the holder's
/// <see cref="EntityDestroyed"/> and creature arrive. <c>CreatureEgg.UpdateProgress</c> keeps calling Hatch every second,
/// so if the lock comes to this game (the holder left) the egg hatches here.
/// </remarks>
public sealed partial class CreatureEgg_Hatch_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CreatureEgg t) => t.Hatch());

    public static bool Prefix(CreatureEgg __instance)
    {
        // This case always destroys the creature egg (see original code)
        if (!__instance.TryGetComponent(out WaterParkItem waterParkItem))
        {
            return true;
        }

        // We don't manage eggs with no id here
        if (!__instance.TryGetNitroxId(out NitroxId eggId))
        {
            return true;
        }

        // It is VERY IMPORTANT to check for simulation ownership on the water park and not on the egg
        // since that's the convention we chose
        if (!waterParkItem.currentWaterPark.TryGetNitroxId(out NitroxId waterParkId))
        {
            return true;
        }

        if (Resolve<SimulationOwnership>().HasAnyLockType(waterParkId))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(eggId));
            return true;
        }

        // Not the tank's simulator: keep the egg until the simulator's hatch arrives
        return false;
    }
}
