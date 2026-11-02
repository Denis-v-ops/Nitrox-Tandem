using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class CyclopsDecoyLaunchButton_OnClick_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsDecoyLaunchButton t) => t.OnClick());

    public static void Prefix(CyclopsDecoyLaunchButton __instance, out int __state)
    {
        __state = __instance.decoyManager.decoyCount;
    }

    public static void Postfix(CyclopsDecoyLaunchButton __instance, int __state)
    {
        // Only when a decoy really left the tube (CyclopsDecoyManager.TryLaunchDecoy), not for a click on an empty tube
        if (__instance.decoyManager.decoyCount < __state && __instance.subRoot.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<Cyclops>().BroadcastLaunchDecoy(id);
        }
    }
}
