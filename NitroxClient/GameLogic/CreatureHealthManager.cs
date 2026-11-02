using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.FMOD;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using static NitroxClient.GameLogic.CreatureDamageRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     Makes the simulating player the only one to change a creature's health so that the damage of every player adds up:<br/>
///     - a player who doesn't simulate a creature doesn't change its health, the hits they cause are sent to the simulating player
///     (<see cref="CreatureDamageAction" />) who applies them like its own<br/>
///     - the simulating player shares the resulting health (<see cref="CreatureHealthChanged" />) so that the next simulating player continues from it
/// </summary>
/// <remarks>
///     Deaths are still replicated by RemoveCreatureCorpse (or EntityDestroyed). Vehicles and the Cyclops are handled by <see cref="LiveMixinManager" />.
/// </remarks>
public class CreatureHealthManager
{
    private const float HEALTH_BROADCAST_INTERVAL = 0.2f;
    private const float HIT_BATCH_INTERVAL = 0.1f;
    private const float OUTGOING_HIT_IDLE_TIME = 5f;
    private const float FORWARDED_HIT_WAIT_TIME = 1f;

    private readonly IPacketSender packetSender;
    private readonly ThrottledPacketSender throttledPacketSender;
    private readonly SimulationOwnership simulationOwnership;
    private readonly AI ai;

    private readonly Dictionary<(NitroxId CreatureId, DamageType Type), OutgoingHit> outgoingHits = [];
    private readonly List<(NitroxId CreatureId, DamageType Type)> idleOutgoingHitKeys = [];
    private readonly List<(CreatureDamageAction Packet, float ReceivedTime)> pendingHits = [];
    private readonly DamageInfo cosmeticDamageInfo = new();

    /// <summary>
    ///     When the health of a creature this game simulates last changed here: tells a late report of the previous simulating
    ///     player from a newer change of our own (<see cref="CreatureDamageRules.AcceptsReportAfterHandOver" />).
    /// </summary>
    private readonly Dictionary<NitroxId, float> localHealthChangeTimeById = [];

    /// <summary>
    ///     Creature currently taking a hit from <see cref="TryApplyForwardedHit" />.
    /// </summary>
    private LiveMixin remoteDamageTarget;

    /// <summary>
    ///     Above 0 while a local player's attack which gives no dealer to LiveMixin.TakeDamage runs (Prawn suit claw and drill, punching a bleeder),
    ///     or the blast of a crash fish this game simulates (Crash_Detonate_Patch).
    /// </summary>
    public int LocalAttackCount;

    public CreatureHealthManager(IPacketSender packetSender, ThrottledPacketSender throttledPacketSender, SimulationOwnership simulationOwnership, AI ai)
    {
        this.packetSender = packetSender;
        this.throttledPacketSender = throttledPacketSender;
        this.simulationOwnership = simulationOwnership;
        this.ai = ai;
    }

    /// <summary>
    ///     Creatures whose health only changes for their simulating player. Their death must be replicable because only that player kills them
    ///     (CreatureDeath_OnKillAsync_Patch, or LiveMixin_Kill_Patch for destroyOnDeath).
    /// </summary>
    public static bool IsHealthSynced(LiveMixin liveMixin, [NotNullWhen(true)] out NitroxId? creatureId)
    {
        creatureId = null;
        return liveMixin.GetComponent<Creature>() &&
               (liveMixin.destroyOnDeath || liveMixin.GetComponent<CreatureDeath>()) &&
               liveMixin.TryGetNitroxId(out creatureId);
    }

    /// <returns>True if the damage must change the local health</returns>
    public bool ShouldApplyDamage(LiveMixin liveMixin, NitroxId creatureId, float originalDamage, Vector3 position, DamageType type, GameObject dealer)
    {
        // A hit forwarded to us, or our own hit sent back because nobody simulates the creature
        if (remoteDamageTarget == liveMixin)
        {
            return true;
        }

        HitSource source = GetHitSource(dealer, type);
        DamageOutcome outcome = CreatureDamageRules.DecideDamage(source, simulationOwnership.HasAnyLockType(creatureId), liveMixin.GetComponent<RemotelyControlled>(),
                                                                 liveMixin.IsAlive() && !liveMixin.invincible, originalDamage > 0f);
        switch (outcome)
        {
            case DamageOutcome.Apply:
                return true;
            case DamageOutcome.Drop:
                return false;
            case DamageOutcome.ForwardAndShow:
                ForwardHit(liveMixin, creatureId, originalDamage, position, type, dealer);
                break;
        }

        // The health stays untouched but the creature still reacts locally like before (effects, flinch, a reaper releasing a grabbed vehicle)
        PlayCosmeticDamage(liveMixin, originalDamage, position, type, dealer);
        return false;
    }

    /// <returns>True if the heal must change the local health</returns>
    public bool ShouldApplyHeal(LiveMixin liveMixin, NitroxId creatureId)
    {
        return simulationOwnership.HasAnyLockType(creatureId) || !liveMixin.GetComponent<RemotelyControlled>();
    }

    /// <summary>
    ///     Applies a hit that the server routed to us, whether or not we hold the lock: the server's routing decides,
    ///     and our own hit comes back when nobody simulates the creature.
    /// </summary>
    public void ReceiveForwardedHit(CreatureDamageAction packet)
    {
        if (!TryApplyForwardedHit(packet))
        {
            pendingHits.Add((packet, Time.time));
        }
    }

    /// <summary>
    ///     Called after a creature's local health changed (LiveMixin.TakeDamage and LiveMixin.AddHealth postfixes).
    /// </summary>
    public void OnHealthChanged(LiveMixin liveMixin)
    {
        if (!IsHealthSynced(liveMixin, out NitroxId creatureId))
        {
            return;
        }

        // Not the bare id: Entities.BroadcastMetadataUpdate throttles EntityMetadataUpdate with the id as key in the same sender
        object dedupeKey = (typeof(CreatureHealthChanged), creatureId);
        // Checked before the lock: CreatureDeath_OnKillAsync_Patch already released it during Kill
        if (!liveMixin.IsAlive())
        {
            throttledPacketSender.RemovePendingPackets(dedupeKey);
            localHealthChangeTimeById.Remove(creatureId);
            return;
        }

        if (simulationOwnership.HasAnyLockType(creatureId))
        {
            localHealthChangeTimeById[creatureId] = Time.time;
            throttledPacketSender.SendThrottled(new CreatureHealthChanged(creatureId, liveMixin.health), _ => dedupeKey, HEALTH_BROADCAST_INTERVAL);
        }
    }

    public void ApplyRemoteHealth(CreatureHealthChanged packet)
    {
        if (!(packet.Health > 0f))
        {
            return;
        }

        // The simulating player's own value is the real one, which also makes it ignore late reports of the previous simulating player,
        // except the last one right after the lock moved here: it can still be on its way (its throttle held it back during the hand-over)
        // and carries hits we haven't got. The server lets it through for the same window (CreatureHealthChangedProcessor).
        if (simulationOwnership.HasAnyLockType(packet.CreatureId) && !IsInHandOverGrace(packet.CreatureId))
        {
            return;
        }

        // The id can belong to what the creature turned into (e.g. a cooked fish), and a dead creature only changes through RemoveCreatureCorpse
        if (!NitroxEntity.TryGetComponentFrom(packet.CreatureId, out LiveMixin liveMixin) || !liveMixin.IsAlive() || !liveMixin.GetComponent<Creature>())
        {
            return;
        }

        // Written directly so that no damage receiver, effect or death runs. Never 0, otherwise RemoveCreatureCorpseProcessor would skip the death replay
        liveMixin.health = Mathf.Min(packet.Health, liveMixin.maxHealth);
        // Cold and poison damage heal back over time on the simulating player's side, whose health is what we just received
        if (liveMixin.tempDamage > 0f)
        {
            liveMixin.tempDamage = 0f;
        }
    }

    private bool IsInHandOverGrace(NitroxId creatureId)
    {
        if (!simulationOwnership.TryGetLockAge(creatureId, out float secondsSinceLockTaken))
        {
            return false;
        }
        float? secondsSinceLocalChange = localHealthChangeTimeById.TryGetValue(creatureId, out float changeTime) ? Time.time - changeTime : null;
        return AcceptsReportAfterHandOver(secondsSinceLockTaken, secondsSinceLocalChange);
    }

    /// <summary>
    ///     Called every frame by <see cref="Multiplayer" />.
    /// </summary>
    public void Update()
    {
        float now = Time.time;
        FlushOutgoingHits(now);
        RetryPendingHits(now);
    }

    private HitSource GetHitSource(GameObject dealer, DamageType type)
    {
        // Prawn suit claw and drill, punching a bleeder: no dealer (see the *_OnHit_Patch scopes)
        if (LocalAttackCount > 0)
        {
            return HitSource.Local;
        }
        if (!dealer)
        {
            return HitSource.World;
        }
        // Knife and thermoblade
        if (dealer == Player.mainObject)
        {
            return HitSource.Local;
        }
        // Seamoth perimeter defense: spawned under the seamoth for its pilot (SeaMoth.OnUpgradeModuleUse) and as a copy for the others (SeamothModuleActionProcessor)
        if (dealer.GetComponent<ElectricalDefense>())
        {
            Vehicle vehicle = dealer.GetComponentInParent<Vehicle>();
            return vehicle && IsSimulatedLocally(vehicle.gameObject) ? HitSource.Local : HitSource.RemoteCopy;
        }
        if (dealer.TryGetComponent(out Creature creature))
        {
            // Sea dragon attacks only run for the sea dragon's simulating player (SeaDragonMeleeAttack_*_Patch)
            if (dealer.GetComponent<SeaDragon>())
            {
                return IsSimulatedLocally(dealer) ? HitSource.Local : HitSource.RemoteCopy;
            }
            // A creature thrown with a propulsion cannon deals impact damage (always Collide) like any thrown object, its other attacks don't change
            if (type != DamageType.Collide || !dealer.GetComponent<PropulseCannonAmmoHandler>())
            {
                // M5: other creatures attack only in their simulating game (FIX_SPEC S14), except the aggression-synced leviathans
                return CreatureDamageRules.CreatureDealerSource(ai.AttacksPlayersFromSimulator(creature), IsSimulatedLocally(dealer), IsSimulatedRemotely(dealer));
            }
        }
        // Only the local player's repulsion cannon throws an object which we don't simulate (propulsion cannon grabs take the lock),
        // and this throw isn't replicated to the other players
        if (type == DamageType.Collide && dealer.GetComponent<PropulseCannonAmmoHandler>() && !IsSimulatedLocally(dealer))
        {
            return HitSource.Local;
        }
        // Impacts of vehicles, the Cyclops and thrown objects: DealDamageOnImpact passes its own game object
        if (dealer.GetComponent<DealDamageOnImpact>())
        {
            if (IsSimulatedLocally(dealer))
            {
                return HitSource.Local;
            }
            return IsSimulatedRemotely(dealer) ? HitSource.RemoteCopy : HitSource.World;
        }
        return HitSource.World;
    }

    private bool IsSimulatedLocally(GameObject gameObject)
    {
        return gameObject.TryGetNitroxId(out NitroxId id) && simulationOwnership.HasAnyLockType(id);
    }

    private static bool IsSimulatedRemotely(GameObject gameObject)
    {
        // Vehicles and the Cyclops simulated by another player are moved by a MovementReplicator, other entities by RemotelyControlled
        return gameObject.GetComponent<MovementReplicator>() || gameObject.GetComponent<RemotelyControlled>();
    }

    private void ForwardHit(LiveMixin liveMixin, NitroxId creatureId, float originalDamage, Vector3 position, DamageType type, GameObject dealer)
    {
        (NitroxId CreatureId, DamageType Type) key = (creatureId, type);
        if (!outgoingHits.TryGetValue(key, out OutgoingHit hit))
        {
            hit = new OutgoingHit();
            outgoingHits.Add(key, hit);
        }

        NitroxId? dealerId = null;
        if (dealer)
        {
            dealer.TryGetNitroxId(out dealerId);
        }

        hit.PendingDamage += originalDamage;
        // Like in LiveMixin.TakeDamage, no position means the creature's position
        hit.PendingLocalPosition = position == default(Vector3) ? Vector3.zero : liveMixin.transform.InverseTransformPoint(position);
        hit.PendingDealerId = dealerId;

        // The first hit goes immediately, the next ones within HIT_BATCH_INTERVAL are summed (e.g. the drill) and sent by FlushOutgoingHits
        float now = Time.time;
        if (now >= hit.LastSendTime + HIT_BATCH_INTERVAL)
        {
            SendHit(creatureId, type, hit, now);
        }
    }

    private void SendHit(NitroxId creatureId, DamageType type, OutgoingHit hit, float now)
    {
        Log.Debug($"[{nameof(CreatureHealthManager)}] Sending a hit of {hit.PendingDamage} ({type}) on creature {creatureId} to its simulating player");
        packetSender.Send(new CreatureDamageAction(creatureId, hit.PendingDamage, type, hit.PendingLocalPosition.ToDto(), Optional.OfNullable(hit.PendingDealerId), 0));
        hit.LastSendTime = now;
        hit.PendingDamage = 0f;
        hit.PendingDealerId = null;
    }

    private void FlushOutgoingHits(float now)
    {
        foreach (KeyValuePair<(NitroxId CreatureId, DamageType Type), OutgoingHit> pair in outgoingHits)
        {
            OutgoingHit hit = pair.Value;
            if (hit.PendingDamage > 0f)
            {
                if (now >= hit.LastSendTime + HIT_BATCH_INTERVAL)
                {
                    SendHit(pair.Key.CreatureId, pair.Key.Type, hit, now);
                }
            }
            else if (now >= hit.LastSendTime + OUTGOING_HIT_IDLE_TIME)
            {
                idleOutgoingHitKeys.Add(pair.Key);
            }
        }

        foreach ((NitroxId CreatureId, DamageType Type) key in idleOutgoingHitKeys)
        {
            outgoingHits.Remove(key);
        }
        idleOutgoingHitKeys.Clear();
    }

    private void RetryPendingHits(float now)
    {
        for (int i = pendingHits.Count - 1; i >= 0; i--)
        {
            (CreatureDamageAction packet, float receivedTime) = pendingHits[i];
            if (TryApplyForwardedHit(packet))
            {
                pendingHits.RemoveAt(i);
                continue;
            }
            if (now < receivedTime + FORWARDED_HIT_WAIT_TIME)
            {
                continue;
            }
            pendingHits.RemoveAt(i);
            Reroute(packet);
        }
    }

    private void Reroute(CreatureDamageAction packet)
    {
        if (packet.Hops >= CreatureDamageAction.MAX_HOPS)
        {
            Log.Debug($"[{nameof(CreatureHealthManager)}] Lost a hit of {packet.OriginalDamage} on creature {packet.CreatureId}: it isn't loaded here");
            return;
        }

        // We don't have the creature (anymore): the server sends the hit to whoever simulates it now
        packetSender.Send(new CreatureDamageAction(packet.CreatureId, packet.OriginalDamage, packet.Type, packet.LocalPosition, packet.DealerId, (byte)(packet.Hops + 1)));
    }

    /// <returns>False if the creature isn't loaded (yet), true if the hit was applied or dropped</returns>
    private bool TryApplyForwardedHit(CreatureDamageAction packet)
    {
        // Not spawned yet (the lock is deferred until SpawnEntities is done) or already unloaded
        if (!NitroxEntity.TryGetComponentFrom(packet.CreatureId, out LiveMixin liveMixin))
        {
            return false;
        }

        // Late hit on a dead creature, or the id now belongs to what the creature turned into (e.g. a cooked fish)
        if (!liveMixin.IsAlive() || !liveMixin.GetComponent<Creature>())
        {
            return true;
        }

        GameObject dealer = null;
        if (packet.DealerId.HasValue && NitroxEntity.TryGetObjectFrom(packet.DealerId.Value, out GameObject dealerObject))
        {
            dealer = dealerObject;
        }

        LiveMixin previousTarget = remoteDamageTarget;
        remoteDamageTarget = liveMixin;
        try
        {
            // A regular TakeDamage, NOT inside LiveMixinManager.IsRemoteHealthChanging: the creature reacts (FleeOnDamage, AggressiveOnDamage),
            // CreatureDeath.lastDamageWasHeat is set, and a death spawns the respawner and is broadcast (CreatureDeath_OnKillAsync_Patch)
            liveMixin.TakeDamage(packet.OriginalDamage, liveMixin.transform.TransformPoint(packet.LocalPosition.ToUnity()), packet.Type, dealer);
        }
        finally
        {
            remoteDamageTarget = previousTarget;
        }
        return true;
    }

    /// <summary>
    ///     What LiveMixin.TakeDamage does, without changing the health and without the low-health looping effect and Kill.
    /// </summary>
    private void PlayCosmeticDamage(LiveMixin liveMixin, float originalDamage, Vector3 position, DamageType type, GameObject dealer)
    {
        float damage = liveMixin.shielded ? 0f : DamageSystem.CalculateDamage(originalDamage, type, liveMixin.gameObject, dealer);
        Vector3 hitPosition = position == default(Vector3) ? liveMixin.transform.position : position;

        // The simulating player plays and broadcasts the real sounds
        using (FMODSystem.SuppressSendingSounds())
        {
            // DamageInfo.Clear doesn't reset the dealer
            cosmeticDamageInfo.Clear();
            cosmeticDamageInfo.originalDamage = originalDamage;
            cosmeticDamageInfo.damage = damage;
            cosmeticDamageInfo.position = hitPosition;
            cosmeticDamageInfo.type = type;
            cosmeticDamageInfo.dealer = dealer;
            liveMixin.NotifyAllAttachedDamageReceivers(cosmeticDamageInfo);

            if (liveMixin.shielded)
            {
                return;
            }

            if (liveMixin.damageClip && damage > 0f && damage >= liveMixin.minDamageForSound && type != DamageType.Radiation)
            {
                global::Utils.PlayEnvSound(liveMixin.damageClip, hitPosition, 20f);
            }
        }

        if (type == DamageType.Electrical)
        {
            if (liveMixin.electricalDamageEffect && Time.time > liveMixin.timeLastElecDamageEffect + 2.5f)
            {
                FixedBounds fixedBounds = liveMixin.GetComponent<FixedBounds>();
                Bounds bounds = fixedBounds ? fixedBounds.bounds : global::UWE.Utils.GetEncapsulatedAABB(liveMixin.gameObject, -1);
                GameObject effect = global::UWE.Utils.InstantiateWrap(liveMixin.electricalDamageEffect, bounds.center, Quaternion.identity);
                effect.transform.parent = liveMixin.transform;
                effect.transform.localScale = bounds.size * 0.65f;
                liveMixin.timeLastElecDamageEffect = Time.time;
            }
            return;
        }

        if (damage > 0f && liveMixin.damageEffect && Time.time > liveMixin.timeLastDamageEffect + 1f &&
            type is DamageType.Normal or DamageType.Collide or DamageType.Explosive or DamageType.Puncture or DamageType.LaserCutter or DamageType.Drill)
        {
            global::Utils.SpawnPrefabAt(liveMixin.damageEffect, liveMixin.transform, hitPosition);
            liveMixin.timeLastDamageEffect = Time.time;
        }
    }

    private sealed class OutgoingHit
    {
        public float LastSendTime = float.MinValue;
        public float PendingDamage;
        public Vector3 PendingLocalPosition;
        public NitroxId? PendingDealerId;
    }
}
