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

    /// <summary>
    ///     The rescue window (COOP_FEATURES_SPEC T1): the one place a kill of the local player is turned into going down.
    ///     <c>Kill</c> comes from <c>TakeDamage</c> at 0 health, from drowning (<c>Player.SuffocationDie</c>), the Cyclops
    ///     explosion and the console; <see cref="RescueWindow.TryGoDown" /> decides which go through.
    /// </summary>
    public static bool Prefix(LiveMixin __instance, DamageType damageType)
    {
        if (!Player.main || __instance != Player.main.liveMixin)
        {
            return true;
        }
        return !Resolve<RescueWindow>().TryGoDown(damageType);
    }

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

        // Players (local or remote) carry the player's id, so EntityDestroyed would delete the player on the server
        if (IsPlayer(__instance))
        {
            return;
        }

        // Creature corpses are synced by CreatureDeath_OnKillAsync_Patch, destroying them here would remove the corpse.
        // Creatures still use the destroyOnDeath and ShouldBroadcastDeath cases, which LiveMixinManager.ReplayRemoteKill skips.
        bool broadcastsKill = (__instance.broadcastKillOnDeath || __instance.passDamageDataOnDeath) && !__instance.GetComponent<CreatureDeath>();
        if (__instance.destroyOnDeath ||
            broadcastsKill ||
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
