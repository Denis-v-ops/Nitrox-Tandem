using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LiveMixin_TakeDamage_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((LiveMixin t) => t.TakeDamage(default(float), default(Vector3), default(DamageType), default(GameObject)));

    public static bool Prefix(out float __state, LiveMixin __instance, float originalDamage, Vector3 position, DamageType type, GameObject dealer)
    {
        // Persist the previous health value
        __state = __instance.health;

        // COOP_FEATURES_SPEC T1: lava and heat still kill a downed player, whose invincibility would ignore them
        if (type == DamageType.Heat && RescueWindow.IsLocalPlayerDowned && Player.main && __instance == Player.main.liveMixin)
        {
            __instance.Kill(type);
            return false;
        }

        // Only the simulating player changes a creature's health (see CreatureHealthManager)
        if (CreatureHealthManager.IsHealthSynced(__instance, out NitroxId creatureId))
        {
            return Resolve<CreatureHealthManager>().ShouldApplyDamage(__instance, creatureId, originalDamage, position, type, dealer);
        }

        if (!Resolve<LiveMixinManager>().IsWhitelistedUpdateType(__instance))
        {
            return true; // everyone should process this locally
        }

        if (Resolve<LiveMixinManager>().ShouldApplyNextHealthUpdate(__instance, dealer))
        {
            return true;
        }
        RelayCreatureAttackOnVehicle(__instance, dealer, originalDamage, type);
        return false;
    }

    /// <summary>
    /// COOP_FEATURES_SPEC D4: a creature we simulate bit a vehicle whose lock another player holds (the pilot). Only that
    /// player's game may change the vehicle's health, and its copy of the creature never bites (see MeleeAttack_CanDealDamageTo_Patch),
    /// so the bite is sent there.
    /// </summary>
    private static void RelayCreatureAttackOnVehicle(LiveMixin vehicle, GameObject dealer, float damage, DamageType type)
    {
        if (!dealer || damage <= 0 || Resolve<LiveMixinManager>().IsRemoteHealthChanging)
        {
            return;
        }
        Creature creature = dealer.GetComponentInParent<Creature>();
        if (!Resolve<AI>().AttacksPlayersFromSimulator(creature) || !creature.TryGetNitroxId(out NitroxId creatureId) ||
            !Resolve<SimulationOwnership>().HasAnyLockType(creatureId) || !vehicle.TryGetNitroxId(out NitroxId vehicleId))
        {
            return;
        }

        Resolve<IPacketSender>().Send(new CreatureAttackedVehicle(creatureId, vehicleId, damage, (ushort)type));
    }

    public static void Postfix(float __state, LiveMixin __instance, float originalDamage, DamageType type, GameObject dealer, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return;
        }

        // IsRemoteHealthChanging means we're replicating an action from the server and BaseCell is managed by BaseLeakManager
        if (Resolve<LiveMixinManager>().IsRemoteHealthChanging || __instance.GetComponent<BaseCell>())
        {
            return;
        }

        // Damage to other players' bodies is always 0 (they're invincible) so we need to check for it before the regular case
        if (HandlePvP(__instance, dealer, originalDamage) || HandleCreatureAttack(__instance, dealer, originalDamage, type))
        {
            return;
        }

        // At this point, if the victim didn't take damage, there's no point in broadcasting it
        if (__state == __instance.health)
        {
            return;
        }

        Resolve<CreatureHealthManager>().OnHealthChanged(__instance);
        BroadcastDefaultTookDamage(__instance);
    }

    private static bool HandlePvP(LiveMixin liveMixin, GameObject dealer, float damage)
    {
        if (!liveMixin.TryGetComponent(out RemotePlayerIdentifier remotePlayerIdentifier))
        {
            return false;
        }

        // Dealer must be the local player, and we need to know about the item they're holding
        if (dealer != Player.mainObject || !Inventory.main.GetHeldObject())
        {
            return false;
        }
        
        PvPAttack.AttackType attackType;
        switch (Inventory.main.GetHeldTool())
        {
            case HeatBlade:
                attackType = PvPAttack.AttackType.HeatbladeHit;
                break;
            case Knife:
                attackType = PvPAttack.AttackType.KnifeHit;
                break;
            default:
                // We don't want to send non-registered attacks
                return false;
        }

        Resolve<IPacketSender>().Send(new PvPAttack(remotePlayerIdentifier.RemotePlayer.SessionId, damage, attackType));
        return true;
    }

    /// <summary>
    /// FIX_SPEC S14: a creature we simulate hurt another player's body (see MeleeAttack_CanDealDamageTo_Patch). The body
    /// can't take damage, so the player's own game applies it.
    /// </summary>
    private static bool HandleCreatureAttack(LiveMixin liveMixin, GameObject dealer, float damage, DamageType type)
    {
        if (!dealer || damage <= 0 || !liveMixin.TryGetComponent(out RemotePlayerIdentifier remotePlayerIdentifier))
        {
            return false;
        }
        Creature creature = dealer.GetComponentInParent<Creature>();
        if (!Resolve<AI>().AttacksPlayersFromSimulator(creature) || !creature.TryGetNitroxId(out NitroxId creatureId) ||
            !Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            return false;
        }

        Resolve<IPacketSender>().Send(new CreatureAttackedPlayer(creatureId, remotePlayerIdentifier.RemotePlayer.SessionId, damage, (ushort)type));
        return true;
    }

    private static void BroadcastDefaultTookDamage(LiveMixin liveMixin)
    {
        // Let others know if we have a lock on this entity
        if (liveMixin.TryGetIdOrWarn(out NitroxId id) && Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(liveMixin.gameObject);

            if (metadata.HasValue)
            {
                Resolve<Entities>().BroadcastMetadataUpdate(id, metadata.Value);
            }
        }
    }
}
