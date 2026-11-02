using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Settings;
using NitroxClient.MonoBehaviours.Cyclops;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public abstract class MovementReplicator : MonoBehaviour
{
    public const float INTERPOLATION_TIME = 4 * MovementBroadcaster.BROADCAST_PERIOD;
    public const float SNAPSHOT_EXPIRATION_TIME = 5f * INTERPOLATION_TIME;

    private TimeManager timeManager;

    private readonly ExpandableRingBuffer<Snapshot> buffer = [];
    /// <summary>
    /// To ensure a smooth experience, we need a max allowed latency value which should top the incoming latencies at all times.
    /// Big increments and any decrements of this value will likely cause stutter, so we try to avoid changing this value too much.
    /// But it is required that after a lag spike, we eventually lower down that value, which is done periodically <see cref="NitroxPrefs.LatencyUpdatePeriod"/>.
    /// </summary>
    public float MaxAllowedLatency;

    private double latestLatencyBumpTime;
    private float maxLatencyDetectedRecently;

    /// <summary>
    /// When encountering a latency bump, we must expect worse happening right after, so we add this margin to our new <see cref="MaxAllowedLatency"/>.
    /// After each periodical latency update (<see cref="LatencyUpdatePeriod"/>), we only want to lower the latency if it's way smaller than the current variable latency.
    /// The safety threshold is defined by this value.
    /// </summary>
    private float SafetyLatencyMargin => NitroxPrefs.SafetyLatencyMargin.Value;

    private float LatencyUpdatePeriod => NitroxPrefs.LatencyUpdatePeriod.Value;

    private Rigidbody rigidbody;
    public NitroxId objectId { get; private set; }

    /// <summary>
    /// Used time must be based on real time to avoid effects from time changes/speed.
    /// A double: it's the world's active play time, which a float only holds to 15.6 ms after 36 h (<see cref="MovementTime"/>).
    /// </summary>
    private double RealTime => timeManager.RealTimeElapsed;

    public void Awake()
    {
        timeManager = this.Resolve<TimeManager>();
    }

    public void AddSnapshot(MovementData movementData, double snapshotRealTime)
    {
        double realTime = RealTime;
        // Packets are processed once per frame, so a packet waited here for up to the time since the previous processing.
        // After a long frame (cells loading, a hitch) every packet looks late by that much, but that isn't network
        // latency and the snapshots are still in time: counting it kept the object 0.3 s further behind for 10+ s after
        // a single 300 ms frame, then made it skip ahead when the latency was lowered again (FIX_SPEC S13).
        float latency = Mathf.Max(0f, MovementTime.Age(realTime, snapshotRealTime) - Multiplayer.PacketWaitBound);

        RecalculateMaxAllowedLatency(latency, realTime);

        double occurrenceTime = snapshotRealTime + INTERPOLATION_TIME + MaxAllowedLatency;

        // Cleaning any previous value change that would occur later than the newly received snapshot
        while (!buffer.IsEmpty())
        {
            if (buffer.Last.IsSnapshotNewer(occurrenceTime))
            {
                buffer.RemoveLast();
            }
            else
            {
                break;
            }
        }

        buffer.Add(new Snapshot(movementData, occurrenceTime, snapshotRealTime));
    }

    private void RecalculateMaxAllowedLatency(float latency, double realTime)
    {
        if (latency > MaxAllowedLatency)
        {
            MaxAllowedLatency = latency + SafetyLatencyMargin;
            latestLatencyBumpTime = realTime;
            maxLatencyDetectedRecently = 0;
            return;
        }

        maxLatencyDetectedRecently = Mathf.Max(latency, maxLatencyDetectedRecently);

        if (realTime - latestLatencyBumpTime < LatencyUpdatePeriod)
        {
            return;
        }

        if (maxLatencyDetectedRecently < MaxAllowedLatency - 2 * SafetyLatencyMargin)
        {
            MaxAllowedLatency = maxLatencyDetectedRecently + SafetyLatencyMargin; // regular gameplay latency variation
        }
        latestLatencyBumpTime = realTime;
        maxLatencyDetectedRecently = 0;
    }

    public void ClearBuffer() => buffer.Clear();

    /// <summary>
    ///     Called when the local player takes over the simulation. This game shows the object a replication delay behind
    ///     where its owner had it, moved by its transform, so its body has no velocity: continuing from there, the object
    ///     stopped dead and jumped back by the delay for the previous owner (FIX_SPEC S13, the Cyclops when the partner
    ///     takes the helm while it moves). Puts it where the owner last had it (the latest snapshot, carried forward by its
    ///     age) with the latest snapshots' velocity instead.
    /// </summary>
    public void HandOverMotion()
    {
        // Disabled: a docked vehicle, which its dock places
        if (!enabled || !rigidbody || rigidbody.isKinematic || buffer.Count < 2)
        {
            return;
        }

        Snapshot previous = buffer[buffer.Count - 2];
        Snapshot latest = buffer.Last;
        float deltaTime = MovementTime.Age(latest.SentTime, previous.SentTime);
        // Clamped: on two PCs the clocks can differ by more than the network latency
        float age = Mathf.Max(0f, MovementTime.Age(RealTime, latest.SentTime));
        // Old snapshots: the owner stopped sending because the object stopped moving
        if (deltaTime <= 0f || age > SNAPSHOT_EXPIRATION_TIME)
        {
            return;
        }

        Vector3 velocity = (latest.Data.Position.ToUnity() - previous.Data.Position.ToUnity()) / deltaTime;
        Quaternion rotationDelta = latest.Data.Rotation.ToUnity() * Quaternion.Inverse(previous.Data.Rotation.ToUnity());
        rotationDelta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            angle -= 360f;
        }
        Vector3 angularVelocity = float.IsNaN(axis.x) || float.IsInfinity(axis.x) ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / deltaTime);

        transform.position = latest.Data.Position.ToUnity() + velocity * age;
        transform.rotation = Quaternion.AngleAxis(angularVelocity.magnitude * age * Mathf.Rad2Deg, angularVelocity.normalized) * latest.Data.Rotation.ToUnity();
        rigidbody.position = transform.position;
        rigidbody.rotation = transform.rotation;
        rigidbody.velocity = velocity;
        rigidbody.angularVelocity = angularVelocity;
        // Destroy waits for the end of the frame: this Update mustn't put the delayed pose back meanwhile
        enabled = false;
    }

    public void Start()
    {
        if (!gameObject.TryGetNitroxId(out NitroxId _objectId))
        {
            Log.Error($"Can't start a {nameof(MovementReplicator)} on {name} because it doesn't have an attached: {nameof(NitroxEntity)}");
            Destroy(this);
            return;
        }
        objectId = _objectId;

        rigidbody = GetComponent<Rigidbody>();
        if (gameObject.TryGetComponent(out NitroxCyclops nitroxCyclops))
        {
            nitroxCyclops.SetReceiving();
        }
        else
        {
            if (gameObject.TryGetComponent(out WorldForces worldForces))
            {
                worldForces.enabled = false;
            }
            rigidbody.isKinematic = false;
        }

        MovementBroadcaster.RegisterReplicator(this);
    }

    public void OnDestroy()
    {
        if (gameObject.TryGetComponent(out NitroxCyclops nitroxCyclops))
        {
            nitroxCyclops.SetBroadcasting();
        }
        else
        {
            if (gameObject.TryGetComponent(out WorldForces worldForces))
            {
                worldForces.enabled = true;
            }
        }

        MovementBroadcaster.UnregisterReplicator(this);
    }

    public void Update()
    {
        if (buffer.IsEmpty())
        {
            return;
        }

        double realTime = RealTime;

        // Sorting out expired nodes
        while (!buffer.IsEmpty() && buffer.First.IsExpired(realTime))
        {
            buffer.RemoveFirst();
        }

        // No usable nodes left
        if (buffer.IsEmpty())
        {
            return;
        }

        // Current node is not useable yet
        if (buffer.First.IsSnapshotNewer(realTime))
        {
            return;
        }

        // Purging the next nodes if they should have already happened
        while (buffer.Count > 1)
        {
            if (!buffer[1].IsSnapshotNewer(realTime))
            {
                buffer.RemoveFirst();
            }
            else
            {
                break;
            }
        }

        // Need at least two snapshots to interpolate
        if (buffer.Count < 2)
        {
            return;
        }

        // Interpolation
        Snapshot previousSnapshot = buffer.First;
        Snapshot nextSnapshot = buffer[1];

        float t = MovementTime.InterpolationFactor(realTime, previousSnapshot.Time, nextSnapshot.Time);

        transform.position = Vector3.Lerp(previousSnapshot.Data.Position.ToUnity(), nextSnapshot.Data.Position.ToUnity(), t);
        
        transform.rotation = Quaternion.Lerp(previousSnapshot.Data.Rotation.ToUnity(), nextSnapshot.Data.Rotation.ToUnity(), t);

        ApplyNewMovementData(nextSnapshot.Data);

        // TODO: fix remote players being able to go through the object (ex: cyclops)
    }

    public abstract void ApplyNewMovementData(MovementData newMovementData);

    /// <param name="Time">When the snapshot is shown (sent time + interpolation delay + latency allowance).</param>
    /// <param name="SentTime">When the owner sent it.</param>
    public record struct Snapshot(MovementData Data, double Time, double SentTime)
    {
        public bool IsSnapshotNewer(double comparedTime) => comparedTime < Time;

        public bool IsExpired(double comparedTime) => comparedTime > Time + SNAPSHOT_EXPIRATION_TIME;
    }

    public static MovementReplicator AddReplicatorToObject(GameObject gameObject)
    {
        if (gameObject.GetComponent<SeaMoth>())
        {
            return gameObject.AddComponent<SeamothMovementReplicator>();
        }
        if (gameObject.GetComponent<Exosuit>())
        {
            return gameObject.AddComponent<ExosuitMovementReplicator>();
        }
        if (gameObject.GetComponent<SubControl>())
        {
            return gameObject.AddComponent<CyclopsMovementReplicator>();
        }
        return gameObject.AddComponent<MovementReplicator>();
    }
}
