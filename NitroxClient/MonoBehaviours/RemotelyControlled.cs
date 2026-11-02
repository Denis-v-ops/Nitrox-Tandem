using NitroxClient.GameLogic;
using NitroxClient.Unity.Smoothing;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public class RemotelyControlled : MonoBehaviour
{
    /// <summary>A pose is carried forward by its velocity for at most this long (s); after that the copy waits at that point.</summary>
    public const float MAX_EXTRAPOLATION = 1f;

    /// <summary>The copy closes its distance to where the creature is with this time constant (s).</summary>
    public const float POSITION_CORRECTION_TIME = 0.25f;

    /// <inheritdoc cref="POSITION_CORRECTION_TIME" />
    public const float ROTATION_CORRECTION_TIME = 0.2f;

    /// <summary>Further than this (m) from where the creature is, the copy is put there (a new copy, or one woken up after a while).</summary>
    public const float TELEPORT_DISTANCE = 15f;

    private readonly SmoothVector smoothPosition = new SmoothVector();
    private readonly SmoothRotation smoothRotation = new SmoothRotation();

    private SwimBehaviour swimBehaviour;
    private WalkBehaviour walkBehaviour;
    private Rigidbody rigidbody;
    private TimeManager timeManager;

    private bool hasCreatureMovement;
    private Vector3 creaturePosition;
    private Quaternion creatureRotation;
    private Vector3 creatureVelocity;
    private Vector3 creatureAngularVelocity;
    private double creatureRealTime;

    private Locomotion disabledLocomotion;
    private WorldForces disabledWorldForces;

    public void Awake()
    {
        swimBehaviour = gameObject.GetComponent<SwimBehaviour>();
        walkBehaviour = gameObject.GetComponent<WalkBehaviour>();
        rigidbody = gameObject.GetComponent<Rigidbody>();
        timeManager = this.Resolve<TimeManager>();
    }

    public void FixedUpdate()
    {
        if (hasCreatureMovement)
        {
            FollowCreatureMovement();
            return;
        }

        if (swimBehaviour || walkBehaviour || !rigidbody)
        {
            return;
        }

        smoothPosition.FixedUpdate();
        smoothRotation.FixedUpdate();

        rigidbody.isKinematic = false;
        rigidbody.velocity = MovementHelper.GetCorrectedVelocity(smoothPosition.Current, Vector3.zero, gameObject, EntityPositionBroadcaster.BROADCAST_INTERVAL);
        rigidbody.angularVelocity = MovementHelper.GetCorrectedAngularVelocity(smoothRotation.Current, Vector3.zero, gameObject, EntityPositionBroadcaster.BROADCAST_INTERVAL);
    }

    public void UpdateOrientation(Vector3 position, Quaternion rotation)
    {
        TeleportIfTooFar(position, rotation);

        if (swimBehaviour)
        {
            swimBehaviour.SwimTo(position, 3f);
        }

        Transform selfTransform = transform;

        // Entities can lose their swimBehavior (such as if they get killed).  Keep these up-to-date incase that happens.
        smoothPosition.Current = selfTransform.position;
        smoothRotation.Current = selfTransform.rotation;
        smoothPosition.Target = position;
        smoothRotation.Target = rotation;
    }

    /// <summary>
    ///     FIX_SPEC S16b: a living creature's pose from the game that simulates it, taken at <paramref name="realTime" />
    ///     (that game's <see cref="TimeManager.RealTimeElapsed" />). From now on this copy goes where the creature is,
    ///     instead of running its own swim path and being teleported back when it was more than 5 m off.
    /// </summary>
    public void UpdateCreatureMovement(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, double realTime)
    {
        // Sequenced packets don't arrive out of order, but a pose sent before a hand-over can still come after a newer one
        if (hasCreatureMovement && realTime < creatureRealTime)
        {
            return;
        }
        if (!hasCreatureMovement)
        {
            StartFollowingCreatureMovement();
        }
        creaturePosition = position;
        creatureRotation = rotation;
        creatureVelocity = velocity;
        creatureAngularVelocity = angularVelocity;
        creatureRealTime = realTime;
    }

    /// <summary>
    ///     The copy's own movement would fight the replicated one: its swim path (<see cref="SplineFollowing" />, still
    ///     driven by the target it had when this game simulated it or by remote actions) pushes it through
    ///     <see cref="Locomotion" />, and <see cref="WorldForces" /> adds drag and gravity. Both are restored in
    ///     <see cref="OnDestroy" /> (this game takes over, or the creature dies).
    /// </summary>
    private void StartFollowingCreatureMovement()
    {
        hasCreatureMovement = true;
        // Also a WalkBehaviour (it derives from SwimBehaviour), whose Idle stops its OnSurfaceMovement too
        if (swimBehaviour)
        {
            swimBehaviour.Idle();
        }
        if (TryGetComponent(out Locomotion locomotion) && locomotion.enabled)
        {
            locomotion.enabled = false;
            disabledLocomotion = locomotion;
        }
        if (TryGetComponent(out WorldForces worldForces) && worldForces.enabled)
        {
            worldForces.enabled = false;
            disabledWorldForces = worldForces;
        }
    }

    /// <summary>
    ///     Where the creature is now in the simulating game: the latest pose carried forward by its velocity for the time
    ///     since it was taken (dead reckoning). Velocity changes within one send period (0.1 s for leviathans) are the only
    ///     error left, so unlike the buffered interpolation of vehicles (<see cref="MovementReplicator" />), the copy isn't
    ///     shown a replication delay behind: at a Reaper's 15–25 m/s, that delay alone would put it 2–5 m back.
    ///     The body keeps a real velocity (the creature's, plus the correction), so physics, collisions and
    ///     velocity-driven animation (<see cref="AnimateByVelocity" />) work as for a creature that swims by itself.
    /// </summary>
    private void FollowCreatureMovement()
    {
        if (!rigidbody)
        {
            return;
        }

        float age = Mathf.Clamp((float)(timeManager.RealTimeElapsed - creatureRealTime), 0f, MAX_EXTRAPOLATION);
        bool extrapolating = age < MAX_EXTRAPOLATION;
        Vector3 velocity = extrapolating ? creatureVelocity : Vector3.zero;
        Vector3 angularVelocity = extrapolating ? creatureAngularVelocity : Vector3.zero;
        Vector3 targetPosition = creaturePosition + creatureVelocity * age;
        Quaternion targetRotation = creatureRotation;
        float angularSpeed = creatureAngularVelocity.magnitude;
        if (angularSpeed > 0.0001f)
        {
            targetRotation = Quaternion.AngleAxis(angularSpeed * age * Mathf.Rad2Deg, creatureAngularVelocity / angularSpeed) * creatureRotation;
        }

        Vector3 positionError = targetPosition - rigidbody.position;
        if (rigidbody.isKinematic || positionError.sqrMagnitude > TELEPORT_DISTANCE * TELEPORT_DISTANCE)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            rigidbody.position = targetPosition;
            rigidbody.rotation = targetRotation;
            if (!rigidbody.isKinematic)
            {
                rigidbody.velocity = velocity;
                rigidbody.angularVelocity = angularVelocity;
            }
            return;
        }

        rigidbody.velocity = velocity + positionError / POSITION_CORRECTION_TIME;

        (targetRotation * Quaternion.Inverse(rigidbody.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            angle -= 360f;
        }
        if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || Mathf.Abs(angle) < 0.01f)
        {
            rigidbody.angularVelocity = angularVelocity;
        }
        else
        {
            rigidbody.angularVelocity = angularVelocity + axis * (angle * Mathf.Deg2Rad / ROTATION_CORRECTION_TIME);
        }
    }

    public void OnDestroy()
    {
        if (disabledWorldForces)
        {
            disabledWorldForces.enabled = true;
        }
        // A dead creature's Locomotion stays off (Locomotion.OnKill)
        if (disabledLocomotion && (!TryGetComponent(out LiveMixin liveMixin) || liveMixin.IsAlive()))
        {
            disabledLocomotion.enabled = true;
        }
    }

    private void TeleportIfTooFar(Vector3 position, Quaternion rotation)
    {
        Transform selfTransform = transform;

        if ((selfTransform.position - position).sqrMagnitude > 25) // Optimized 5m distance test
        {
            selfTransform.position = position;
            selfTransform.rotation = rotation;
        }
    }

    public static RemotelyControlled Ensure(GameObject gameObject)
    {
        if (gameObject.TryGetComponent(out RemotelyControlled remotelyControlled))
        {
            return remotelyControlled;
        }
        if (gameObject.GetComponent<PipeSurfaceFloater>())
        {
            return gameObject.AddComponent<RemotelyControlledPipeFloater>();
        }
        return gameObject.AddComponent<RemotelyControlled>();
    }
}
