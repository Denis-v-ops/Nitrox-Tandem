using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Bases;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Only one game burns the items of a bioreactor (#2494, FIX_SPEC S7): the one holding the base's simulation lock
/// (<see cref="BaseModuleSimulation"/>). It sends <see cref="EntityDestroyed"/> for each burned item, so the item is gone
/// on the server and in the other games too.
/// </summary>
/// <remarks>
/// <c>BaseBioReactor.ProducePower</c> (game 83031, called from <c>Update</c> while the container isn't empty) adds the
/// requested energy to <c>_toConsume</c>, takes every item whose charge is covered out of the container
/// (<c>RemoveItem(pickupable, forced: true)</c>) and destroys it (<c>Object.Destroy(pickupable.gameObject)</c>), then
/// returns the energy produced. Base power isn't synced, so the other games still return the requested energy while
/// their copy of the container has items: their base stays powered, and they stop once the producer's burns empty it.
/// </remarks>
public sealed partial class BaseBioReactor_ProducePower_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((BaseBioReactor t) => t.ProducePower(default));

    public static bool Prefix(BaseBioReactor __instance, float requested, ref float __result)
    {
        if (Resolve<BaseModuleSimulation>().IsLocalProducer(__instance))
        {
            return true;
        }

        __result = requested > 0f && __instance.container.count > 0 ? requested : 0f;
        return false;
    }

    /*
     * container.RemoveItem(pickupable, forced: true);
     * UnityEngine.Object.Destroy(pickupable.gameObject);  ---> DestroyBurnedItem(pickupable.gameObject);
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Call, Reflect.Method(() => UnityEngine.Object.Destroy(default))))
                                            .ThrowIfInvalid("Object.Destroy call not found in BaseBioReactor.ProducePower")
                                            .SetOperandAndAdvance(Reflect.Method(() => DestroyBurnedItem(default)))
                                            .InstructionEnumeration();
    }

    public static void DestroyBurnedItem(UnityEngine.Object burnedObject)
    {
        if (burnedObject is GameObject gameObject && gameObject.TryGetNitroxId(out NitroxId id))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(id));
        }
        UnityEngine.Object.Destroy(burnedObject);
    }
}
