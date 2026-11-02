using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Pickupable_Pickup_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((Pickupable p) => p.Pickup(default));

    public static void Prefix(Pickupable __instance)
    {
        Items items = Resolve<Items>();
        // A machine creating an item in its container, announced by ItemContainers.BroadcastItemAdd instead
        if (items.MachineProducingCount > 0)
        {
            return;
        }
        items.PickedUpByPlayer(__instance.gameObject, __instance.GetTechType());
    }
}

