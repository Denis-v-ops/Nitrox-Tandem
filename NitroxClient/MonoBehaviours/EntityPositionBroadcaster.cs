using System.Collections.Generic;
using System.Linq;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Simulation;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.EntityTransformUpdates;

namespace NitroxClient.MonoBehaviours;

public class EntityPositionBroadcaster : MonoBehaviour
{
    public static readonly float BROADCAST_INTERVAL = 0.25f;

    /// <summary>How often the creatures' send periods (<see cref="CreatureMovementPolicy" />) are checked.</summary>
    public const float CREATURE_TICK = 0.05f;

    /// <summary>
    ///     <see cref="CreatureMovements" /> is unreliable, and LiteNetLib refuses an unreliable packet larger than the path
    ///     MTU (508 bytes until its MTU discovery raises it). 5 creatures make about 400 bytes
    ///     (<c>CreatureMovementPolicyTest</c>).
    /// </summary>
    public const int MAX_CREATURES_PER_PACKET = 5;

    private static HashSet<NitroxId> watchingEntityIds = new();

    private static readonly Dictionary<NitroxId, CreatureSendState> creatureStatesById = new();

    /// <summary>Counters for measuring bandwidth through the dev bridge (FIX_SPEC S16b).</summary>
    public static long CreaturePacketsSent;

    /// <inheritdoc cref="CreaturePacketsSent" />
    public static long CreatureMovementsSent;

    private IPacketSender packetSender;
    private SimulationOwnership simulationOwnership;
    private PlayerManager playerManager;
    private TimeManager timeManager;

    private readonly List<Vector3> partnerPositions = [];
    private readonly List<CreatureMovement> creatureBatch = [];

    private float time;
    private float creatureTime;

    public void Awake()
    {
        packetSender = NitroxServiceLocator.LocateService<IPacketSender>();
        simulationOwnership = NitroxServiceLocator.LocateService<SimulationOwnership>();
        playerManager = NitroxServiceLocator.LocateService<PlayerManager>();
        timeManager = NitroxServiceLocator.LocateService<TimeManager>();
    }

    public void Update()
    {
        time += Time.deltaTime;
        creatureTime += Time.unscaledDeltaTime;

        // Only do on a specific cadence to avoid hammering server
        if (time >= BROADCAST_INTERVAL)
        {
            time = 0;

            if (watchingEntityIds.Count > 0)
            {
                Dictionary<NitroxId, GameObject> nonSplineEntitiesById = NitroxEntity.GetObjectsFrom(watchingEntityIds)
                                                                                     .Where(item => !item.Value.GetComponent<SwimBehaviour>() &&
                                                                                                    !item.Value.GetComponent<WalkBehaviour>())
                                                                                     .ToDictionary(item => item.Key, item => item.Value);

                List<EntityTransformUpdate> updates = BuildUpdates(nonSplineEntitiesById);

                if (updates.Count > 0)
                {
                    packetSender.Send(new EntityTransformUpdates(updates));
                }
            }
        }

        if (creatureTime >= CREATURE_TICK)
        {
            creatureTime = 0;
            if (watchingEntityIds.Count > 0)
            {
                BroadcastCreatures();
            }
        }
    }

    private List<EntityTransformUpdate> BuildUpdates(Dictionary<NitroxId, GameObject> nonSplineEntitiesById)
    {
        List<EntityTransformUpdate> updates = new();

        foreach (KeyValuePair<NitroxId, GameObject> gameObjectWithId in nonSplineEntitiesById)
        {
            if (gameObjectWithId.Value)
            {
                updates.Add(new RawTransformUpdate(gameObjectWithId.Key, gameObjectWithId.Value.transform.position.ToDto(), gameObjectWithId.Value.transform.rotation.ToDto()));
            }
        }

        return updates;
    }

    /// <summary>
    ///     FIX_SPEC S16b: the pose and velocity of each living creature this game simulates, at the rate
    ///     <see cref="CreatureMovementPolicy" /> gives it. Replaces the pose that was only sent when the creature's AI
    ///     picked a new swim target (every 1–5 s), which left the partner's copy to drift and then snap 5–17 m.
    /// </summary>
    private void BroadcastCreatures()
    {
        double now = timeManager.RealTimeElapsed;

        partnerPositions.Clear();
        foreach (RemotePlayer remotePlayer in playerManager.GetAll())
        {
            if (remotePlayer.Body)
            {
                partnerPositions.Add(remotePlayer.Body.transform.position);
            }
        }

        foreach (NitroxId id in watchingEntityIds)
        {
            if (!NitroxEntity.TryGetObjectFrom(id, out GameObject gameObject) || !simulationOwnership.HasAnyLockType(id))
            {
                continue;
            }
            if (!creatureStatesById.TryGetValue(id, out CreatureSendState state) || state.GameObject != gameObject)
            {
                creatureStatesById[id] = state = new CreatureSendState(gameObject);
            }
            // Not a creature (sent by BuildUpdates), dead (its corpse keeps its own physics in each game), or asleep with its cell
            if (!state.IsCreature || !state.Rigidbody || !gameObject.activeInHierarchy || (state.LiveMixin && !state.LiveMixin.IsAlive()))
            {
                continue;
            }

            Transform transform = gameObject.transform;
            Vector3 position = transform.position;
            Vector3 velocity = state.Rigidbody.velocity;
            float speed = velocity.magnitude;
            bool moving = CreatureMovementPolicy.IsMoving(speed, state.LastSentSpeed);
            float period = CreatureMovementPolicy.SendPeriod(state.CellLevel, GetPartnerDistance(position), moving);
            float sinceLastSend = state.EverSent ? (float)(now - state.LastSentTime) : float.PositiveInfinity;
            float moved = state.EverSent ? Vector3.Distance(position, state.LastSentPosition) : float.PositiveInfinity;

            // Half a tick early, so that a period isn't stretched to the next tick
            if (!CreatureMovementPolicy.ShouldSend(sinceLastSend + CREATURE_TICK * 0.5f, period, moving, moved))
            {
                continue;
            }

            creatureBatch.Add(new CreatureMovement(id, position.ToDto(), transform.rotation.ToDto(), velocity.ToDto(), state.Rigidbody.angularVelocity.ToDto()));
            state.EverSent = true;
            state.LastSentTime = now;
            state.LastSentPosition = position;
            state.LastSentSpeed = speed;

            if (creatureBatch.Count >= MAX_CREATURES_PER_PACKET)
            {
                SendCreatureBatch(now);
            }
        }
        SendCreatureBatch(now);
    }

    private void SendCreatureBatch(double now)
    {
        if (creatureBatch.Count == 0)
        {
            return;
        }
        packetSender.Send(new CreatureMovements([..creatureBatch], now));
        CreaturePacketsSent++;
        CreatureMovementsSent += creatureBatch.Count;
        creatureBatch.Clear();
    }

    /// <summary>Per-axis distance to the nearest other player: entities load in a box around a player.</summary>
    private float GetPartnerDistance(Vector3 position)
    {
        float nearest = float.PositiveInfinity;
        foreach (Vector3 partner in partnerPositions)
        {
            float distance = Mathf.Max(Mathf.Abs(partner.x - position.x), Mathf.Max(Mathf.Abs(partner.y - position.y), Mathf.Abs(partner.z - position.z)));
            nearest = Mathf.Min(nearest, distance);
        }
        return nearest;
    }

    public static void WatchEntity(NitroxId id)
    {
        watchingEntityIds.Add(id);

        // The game object may not exist at this very moment (due to being spawned in async). This is OK as we will
        // automatically start sending updates when we finally get it in the world. This behavior will also allow us
        // to resync or respawn entities while still have broadcasting enabled without doing anything extra.

        if (NitroxEntity.TryGetComponentFrom(id, out RemotelyControlled remotelyControlled))
        {
            Object.Destroy(remotelyControlled);
        }
    }

    public static void StopWatchingEntity(NitroxId id)
    {
        watchingEntityIds.Remove(id);
        creatureStatesById.Remove(id);
    }

    public static void RemoveEntityMovementControl(GameObject gameObject, NitroxId entityId)
    {
        if (gameObject.TryGetComponent(out RemotelyControlled remotelyControlled))
        {
            Destroy(remotelyControlled);
        }
        StopWatchingEntity(entityId);
    }

    private sealed class CreatureSendState
    {
        public readonly GameObject GameObject;
        public readonly bool IsCreature;
        public readonly Rigidbody Rigidbody;
        public readonly LiveMixin LiveMixin;
        public readonly int CellLevel;

        public bool EverSent;
        public double LastSentTime;
        public Vector3 LastSentPosition;
        public float LastSentSpeed;

        public CreatureSendState(GameObject gameObject)
        {
            GameObject = gameObject;
            IsCreature = gameObject.GetComponent<SwimBehaviour>() || gameObject.GetComponent<WalkBehaviour>();
            Rigidbody = gameObject.GetComponent<Rigidbody>();
            LiveMixin = gameObject.GetComponent<LiveMixin>();
            CellLevel = gameObject.TryGetComponent(out LargeWorldEntity largeWorldEntity) ? (int)largeWorldEntity.cellLevel : 0;
        }
    }
}
