using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LiveMixin_Kill_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((LiveMixin t) => t.Kill(default));

    public static void Postfix(LiveMixin __instance)
    {
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        if (!__instance.TryGetNitroxId(out NitroxId objectId))
        {
            return;
        }

        if (Resolve<LiveMixinManager>().IsWhitelistedUpdateType(__instance))
        {
            return;
        }

        // Creature deaths are synced by CreatureDeath_OnKillAsync_Patch and destroying them here would remove the corpse.
        // Players (local or remote) carry the player's id, so EntityDestroyed would delete the player on the server.
        if (__instance.GetComponent<CreatureDeath>() || IsPlayer(__instance))
        {
            return;
        }

        if (__instance.destroyOnDeath || 
            __instance.broadcastKillOnDeath || 
            __instance.passDamageDataOnDeath || 
            Resolve<LiveMixinManager>().ShouldBroadcastDeath(__instance))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(objectId));
        }
    }

    private static bool IsPlayer(LiveMixin liveMixin)
    {
        return liveMixin.GetComponent<Player>() ||
               (Player.main && Player.main.liveMixin == liveMixin) ||
               liveMixin.GetComponent<RemotePlayerIdentifier>();
    }
}
