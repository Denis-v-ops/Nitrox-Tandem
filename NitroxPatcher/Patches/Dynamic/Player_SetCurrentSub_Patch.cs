using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Broadcasts the local player's sub change once the game has actually applied it.
/// </summary>
/// <remarks>
/// <see cref="Player.SetCurrentSub" /> silently ignores a non-null sub when the sub changed less than 3 physics ticks ago
/// (<c>CanChangeSub</c>), so broadcasting before it runs could tell the server and the partner "inside" while the local game
/// stays outside.
/// </remarks>
public sealed partial class Player_SetCurrentSub_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Player t) => t.SetCurrentSub(default, default));

    public static void Prefix(Player __instance, out SubRoot __state)
    {
        __state = __instance.currentSub;
    }

    public static void Postfix(Player __instance, SubRoot __state)
    {
        // We really want to avoid unnecessary packets giving false information
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        // Only a real change: in the moonpool water, change requests can be hammered while the sub doesn't change.
        SubRoot currentSub = __instance.currentSub;
        if (currentSub == __state)
        {
            return;
        }

        Resolve<LocalPlayer>().BroadcastSubrootChange(currentSub.GetId());
    }
}
