using System.Reflection;
using NitroxClient.MonoBehaviours.Gui.InGame;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Keeps a player ping's own icon and colour (COOP_FEATURES_SPEC F2). <see cref="uGUI_Pings.OnAdd" /> sets the icon
///     from the ping type and the colour from the colour index, and runs for every ping again whenever the component is
///     enabled (<c>OnEnable</c> re-adds them all).
/// </summary>
public sealed partial class uGUI_Pings_OnAdd_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_Pings t) => t.OnAdd(default));

    public static void Postfix(uGUI_Pings __instance, PingInstance instance)
    {
        ApplyPlayerPing(__instance, instance);
    }

    internal static void ApplyPlayerPing(uGUI_Pings pings, PingInstance instance)
    {
        if (instance && instance.TryGetComponent(out PlayerPing playerPing) && pings.pings.TryGetValue(instance.Id, out uGUI_Ping marker))
        {
            playerPing.ApplyTo(marker);
        }
    }
}
