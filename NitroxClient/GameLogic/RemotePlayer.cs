using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using NitroxClient.GameLogic.HUD;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Equipment.Abstract;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;
using NitroxClient.MonoBehaviours.Gui.HUD;
using NitroxClient.MonoBehaviours.Vehicles;
using Nitrox.Model.GameLogic.FMOD;
using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.MultiplayerSession;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

public class RemotePlayer : INitroxPlayer
{
    /// <summary>
    /// Marks <see cref="Player.mainObject"/> and every <see cref="Body"/> so they can be precisely queried (e.g. by sea dragons).
    /// The value (5050) is determined arbitrarily and should not be used already.
    /// </summary>
    public const EcoTargetType PLAYER_ECO_TARGET_TYPE = (EcoTargetType)5050;

    private static readonly int animatorPlayerIn = Animator.StringToHash("player_in");

    private readonly PlayerModelManager playerModelManager;
    private readonly PlayerVitalsManager playerVitalsManager;
    private readonly FMODWhitelist fmodWhitelist;
    private List<IEquipmentVisibilityHandler> equipmentVisibilityHandlers = [];
    private PlayerTool? armsConfiguredFor;
    /// <summary>The partner's own arm states (not a cinematic's) as last received, see <see cref="SetArmState" />.</summary>
    private readonly Dictionary<string, bool> armStates = [];
    private bool cinematicFollowsMovement;

    public PlayerContext PlayerContext { get; }
    public GameObject? Body { get; private set; }
    public GameObject PlayerModel { get; private set; }
    public Rigidbody RigidBody { get; private set; }
    public CapsuleCollider Collider { get; private set; }
    public ArmsController ArmsController { get; private set; }
    public AnimationController AnimationController { get; private set; }
    public ItemsContainer Inventory { get; private set; }
    public Transform ItemAttachPoint { get; private set; }
    public RemotePlayerVitals vitals { get; private set; }

    public SessionId SessionId => PlayerContext.SessionId;
    public string PlayerName => PlayerContext.PlayerName;
    public PlayerSettings PlayerSettings => PlayerContext.PlayerSettings;

    public Vehicle Vehicle { get; private set; }
    public SubRoot SubRoot { get; private set; }
    public EscapePod? EscapePod { get; private set; }
    public PilotingChair PilotingChair { get; private set; }
    public InfectedMixin InfectedMixin { get; private set; }
    public LiveMixin LiveMixin { get; private set; }

    /// <summary>
    ///     The entity (or bed side, <see cref="NitroxClient.MonoBehaviours.CinematicController.BedLockId" />) whose cinematic
    ///     plays on this body (upstream #2859). Null when none does.
    /// </summary>
    public NitroxId? InCinematicEntityId { get; private set; }

    public bool InCinematic => InCinematicEntityId != null;

    /// <summary>
    ///     A cinematic now poses this body: movement packets neither move it nor drive its animator (<see cref="IsPoseLocked" />).
    /// </summary>
    /// <param name="followsMovement">
    ///     Movement packets still place the body (not its animator): on a bench, where the seated player's game sends where
    ///     they sit, and this game's copy of the seat's animated target would be wrong (the seat isn't animated here).
    /// </param>
    public void SetInCinematic(NitroxId entityId, bool followsMovement = false)
    {
        InCinematicEntityId = entityId;
        cinematicFollowsMovement = followsMovement;
        // The partner's game stops sending movement during a cinematic (PlayerMovementBroadcaster): the last velocity must
        // not carry the body off the seat or out of the bed
        if (RigidBody && !RigidBody.isKinematic)
        {
            RigidBody.velocity = Vector3.zero;
            RigidBody.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    ///     The cinematic is over. The pose stays locked if the body is also downed (<see cref="IsPoseLocked" />).
    /// </summary>
    public void ClearInCinematic()
    {
        if (InCinematicEntityId == null)
        {
            return;
        }
        InCinematicEntityId = null;
        RestoreArmStates();
    }

    public readonly Event<RemotePlayer> PlayerDeathEvent = new();

    public readonly Event<RemotePlayer> PlayerDisconnectEvent = new();

    public CyclopsPawn Pawn { get; set; }

    /// <summary>
    ///     The body lies in the downed pose (COOP_FEATURES_SPEC T1, <see cref="DownedPose" />).
    /// </summary>
    public bool IsDowned { get; private set; }

    /// <summary>
    ///     Something holds the body's pose, so movement must not drive its animator: the downed pose (T1) or a cinematic
    ///     (upstream #2859, COOP_FEATURES_SPEC R1b). <see cref="AnimationController" /> tests it every frame, so clearing one
    ///     of them can't free a pose the other still holds. Only a cinematic also stops movement packets from moving the
    ///     body (<see cref="UpdatePosition" />): a downed body must follow its player (T1).
    /// </summary>
    public bool IsPoseLocked => IsDowned || InCinematic;

    public RemotePlayer(PlayerContext playerContext, PlayerModelManager playerModelManager, PlayerVitalsManager playerVitalsManager, FMODWhitelist fmodWhitelist)
    {
        PlayerContext = playerContext;
        this.playerModelManager = playerModelManager;
        this.playerVitalsManager = playerVitalsManager;
        this.fmodWhitelist = fmodWhitelist;
    }

    public void InitializeGameObject(GameObject playerBody)
    {
        Body = playerBody;
        Body.name = PlayerName;

        RigidBody = Body.AddComponent<Rigidbody>();
        RigidBody.useGravity = false;
        RigidBody.interpolation = RigidbodyInterpolation.Interpolate;

        NitroxEntity.SetNewId(Body, PlayerContext.PlayerNitroxId);

        // Get player
        PlayerModel = Body.RequireGameObject("player_view");
        // Move variables to keep player animations from mirroring and for identification
        ArmsController = PlayerModel.GetComponent<ArmsController>();
        ArmsController.smoothSpeedUnderWater = 0;
        ArmsController.smoothSpeedAboveWater = 0;

        // ConditionRules has Player.Main based conditions from ArmsController
        PlayerModel.GetComponent<ConditionRules>().enabled = false;

        AnimationController = PlayerModel.AddComponent<AnimationController>();
        AnimationController.IsPoseLocked = () => IsPoseLocked;

        Transform inventoryTransform = new GameObject("Inventory").transform;
        inventoryTransform.SetParent(Body.transform);
        Inventory = new ItemsContainer(6, 8, inventoryTransform, $"NitroxInventoryStorage_{PlayerName}", null);

        ItemAttachPoint = PlayerModel.transform.Find(PlayerEquipmentConstants.ITEM_ATTACH_POINT_GAME_OBJECT_NAME);

        CoroutineUtils.StartCoroutineSmart(playerModelManager.AttachPing(this));
        playerModelManager.BeginApplyPlayerColor(this);
        equipmentVisibilityHandlers = playerModelManager.RegisterEquipmentVisibilityHandler(PlayerModel);
        SetupBody();
        SetupSkyAppliers();
        SetupPlayerSounds();
        SetupMixins();

        PlayerAnimation animation = PlayerContext.Animation;
        UpdateAnimationAndCollider((AnimChangeType)animation.Type, (AnimChangeState)animation.State);

        vitals = playerVitalsManager.CreateOrFindForPlayer(this);
        RefreshVitalsVisibility();
        Body.AddComponent<PartnerSignal>().Initialize(this);

        PlayerDisconnectEvent.AddHandler(Body, _ =>
        {
            Pawn?.Unregister();
            Pawn = null;
        });

        PlayerDeathEvent.AddHandler(Body, _ =>
        {
            ResetStates();
        });
    }

    public void Attach(Transform transform, bool keepWorldTransform = false)
    {
        Body.transform.SetParent(transform);

        if (!keepWorldTransform)
        {
            UWE.Utils.ZeroTransform(Body);
        }

        SkyEnvironmentChanged.Broadcast(Body, transform);
    }

    public void Detach()
    {
        Body.transform.SetParent(null);
        SkyEnvironmentChanged.Broadcast(Body, (GameObject)null);
    }

    public void UpdatePosition(Vector3 position, Vector3 velocity, Quaternion bodyRotation, Quaternion aimingRotation)
    {
        // It might happen that we get movement packets before the body is actually initialized which is not too bad
        if (!Body)
        {
            return;
        }

        Body.SetActive(true);

        // Skip position updates during cinematics - the cinematic controller handles positioning
        if (InCinematic && !cinematicFollowsMovement)
        {
            return;
        }

        // When receiving movement packets, a player can not be controlling a vehicle (they can walk through subroots though).
        SetVehicle(null);
        SetPilotingChair(null);

        AnimationController.AimingRotation = aimingRotation;
        AnimationController.UpdatePlayerAnimations = true;
        AnimationController.Velocity = MovementHelper.GetCorrectedVelocity(position, velocity, Body, Time.fixedDeltaTime);

        // If in a subroot the position will be relative to the subroot
        if (SubRoot && SubRoot.isBase)
        {
            Quaternion vehicleAngle = SubRoot.transform.rotation;
            position = vehicleAngle * position;
            position += SubRoot.transform.position;
            bodyRotation = vehicleAngle * bodyRotation;
            aimingRotation = vehicleAngle * aimingRotation;
        }

        RigidBody.velocity = AnimationController.Velocity;
        RigidBody.angularVelocity = MovementHelper.GetCorrectedAngularVelocity(bodyRotation, Vector3.zero, Body, Time.fixedDeltaTime);
    }

    public void UpdatePositionInCyclops(Vector3 localPosition, Quaternion localRotation)
    {
        if (Pawn == null || PilotingChair)
        {
            return;
        }

        SetVehicle(null);

        AnimationController.AimingRotation = localRotation;
        AnimationController.UpdatePlayerAnimations = true;
        AnimationController.Velocity = (localPosition - Pawn.Handle.transform.localPosition) / Time.fixedDeltaTime;

        Pawn.Handle.transform.localPosition = localPosition;
        Pawn.Handle.transform.localRotation = localRotation;
    }

    public void SetPilotingChair(PilotingChair newPilotingChair)
    {
        if (PilotingChair != newPilotingChair)
        {
            PilotingChair = newPilotingChair;

            CyclopsMovementReplicator cyclopsMovementReplicator = null;

            // For unexpected and expected cases, for example when a player is driving a cyclops but the cyclops is destroyed
            if (!SubRoot)
            {
                Log.Error("Player changed PilotingChair but is not in SubRoot!");
            }
            else
            {
                cyclopsMovementReplicator = SubRoot.gameObject.EnsureComponent<CyclopsMovementReplicator>();
            }

            if (PilotingChair)
            {
                Attach(PilotingChair.sittingPosition.transform);
                ArmsController.SetWorldIKTarget(PilotingChair.leftHandPlug, PilotingChair.rightHandPlug);

                if (cyclopsMovementReplicator)
                {
                    cyclopsMovementReplicator.Enter(this);
                }

                if (SubRoot)
                {
                    SkyEnvironmentChanged.Broadcast(Body, SubRoot);
                }

                AnimationController.UpdatePlayerAnimations = false;
            }
            else
            {
                SetSubRoot(SubRoot, true);
                ArmsController.SetWorldIKTarget(null, null);

                if (cyclopsMovementReplicator)
                {
                    cyclopsMovementReplicator.Exit();
                }
            }

            bool isKinematic = newPilotingChair;
            UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, isKinematic, true);
            AnimationController["cyclops_steering"] = newPilotingChair;
        }
    }

    public void SetSubRoot(SubRoot newSubRoot, bool force = false)
    {
        if (SubRoot != newSubRoot || force)
        {
            // Unregister from previous cyclops
            Pawn?.Unregister();
            Pawn = null;

            if (newSubRoot)
            {
                Attach(newSubRoot.transform, true);

                // Register in new cyclops
                if (newSubRoot.TryGetComponent(out NitroxCyclops nitroxCyclops))
                {
                    nitroxCyclops.OnPlayerEnter(this);
                }
            }
            else
            {
                Detach();
            }

            SubRoot = newSubRoot;
        }
    }

    public void SetEscapePod(EscapePod? newEscapePod)
    {
        if (EscapePod != newEscapePod)
        {
            if (newEscapePod)
            {
                Attach(newEscapePod.transform, true);
            }
            else
            {
                Detach();
            }

            EscapePod = newEscapePod;
        }
    }

    public void SetVehicle(Vehicle newVehicle)
    {
        if (Vehicle != newVehicle)
        {
            if (Vehicle)
            {
                Vehicle.mainAnimator.SetBool(animatorPlayerIn, false);

                Detach();
                ArmsController.SetWorldIKTarget(null, null);

                if (Vehicle.TryGetComponent(out VehicleMovementReplicator vehicleMovementReplicator))
                {
                    vehicleMovementReplicator.Exit();
                }
            }

            if (newVehicle)
            {
                // Only trigger the enter animation if the vehicle doesn't already have player_in set
                // This prevents replaying the animation during undocking when the player is already in the vehicle
                if (!newVehicle.mainAnimator.GetBool(animatorPlayerIn))
                {
                    newVehicle.mainAnimator.SetBool(animatorPlayerIn, true);
                }

                Attach(newVehicle.playerPosition.transform);
                ArmsController.SetWorldIKTarget(newVehicle.leftHandPlug, newVehicle.rightHandPlug);

                // From here, a basic issue can happen.
                // When a vehicle is docked since we joined a game and another player undocks him before the local player does,
                // no VehicleMovementReplicator can be found on the vehicle because they are only created when receiving SimulationOwnership packets
                // Therefore we need to make sure that the VehicleMovementReplicator component exists before using it
                switch (newVehicle)
                {
                    case SeaMoth:
                        newVehicle.gameObject.EnsureComponent<SeamothMovementReplicator>().Enter(this);
                        break;
                    case Exosuit:
                        newVehicle.gameObject.EnsureComponent<ExosuitMovementReplicator>().Enter(this);
                        break;
                }

                AnimationController.UpdatePlayerAnimations = false;
            }

            bool isKinematic = newVehicle;
            UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, isKinematic, true);

            Vehicle = newVehicle;

            AnimationController["in_seamoth"] = newVehicle is SeaMoth;
            AnimationController["in_exosuit"] = AnimationController["using_mechsuit"] = newVehicle is Exosuit;

            // In case we are dismissing the current seamoth to enter the cyclops through a docking,
            // we need to setup the player back in the cyclops
            if (!newVehicle && SubRoot)
            {
                SetSubRoot(SubRoot, true);
            }
        }
    }

    /// <summary>
    ///     Lays the body down or lets it get up (COOP_FEATURES_SPEC T1). Its position still follows the player's movement
    ///     packets: a downed player can be pushed out of a vehicle, and the rescuer has to find the body where it is.
    /// </summary>
    public void SetDownedPose(bool downed)
    {
        if (IsDowned == downed || !Body)
        {
            return;
        }
        IsDowned = downed;
        if (downed)
        {
            Body.EnsureComponent<DownedPose>().Begin(this);
        }
        else if (Body.TryGetComponent(out DownedPose pose))
        {
            pose.End();
        }
    }

    /// <summary>
    /// Drops the remote player, swimming where he is. Resets its animator.
    /// </summary>
    public void ResetStates()
    {
        SetPilotingChair(null);
        SetVehicle(null);
        SetSubRoot(null);
        ClearInCinematic();
        AnimationController.UpdatePlayerAnimations = true;
        AnimationController.Reset();
        ArmsController.SetWorldIKTarget(null, null);
        // The animator reset cleared holding_tool, and the world-target reconfigure above drops the tool's IK
        armsConfiguredFor = null;
        armStates.Clear();
    }

    public void Destroy()
    {
        Log.Info($"{PlayerName} left the game");
        Log.InGame(Language.main.Get("Nitrox_PlayerLeft").Replace("{PLAYER}", PlayerName));
        ResetStates();
        NitroxEntity.RemoveFrom(Body);
        Object.DestroyImmediate(Body);
    }

    public void UpdateAnimationAndCollider(AnimChangeType type, AnimChangeState state)
    {
        switch (type)
        {
            case AnimChangeType.UNDERWATER:
                AnimationController["is_underwater"] = state != AnimChangeState.OFF;
                break;
            case AnimChangeType.INFECTION_REVEAL:
                AnimationController["player_infected"] = state != AnimChangeState.UNSET;
                break;
            default:
                if (ArmStates.TryGetParameter(type, out string parameter))
                {
                    SetArmState(parameter, state == AnimChangeState.ON);
                }
                break;
        }

        // Rough estimation for different collider boxes in different animation stages
        if (AnimationController["is_underwater"])
        {
            Collider.center = new Vector3(0f, -0.3f, 0f);
            Collider.height = 0.5f;
        }
        else
        {
            Collider.center = new Vector3(0f, -0.8f, 0f);
            Collider.height = 1.5f;
        }
    }

    /// <summary>
    ///     <b>The one place that writes arm and hand states on this body</b> (COOP_FEATURES_SPEC R2b): the synced
    ///     <see cref="ArmStates" /> (tool use, PDA, surface swimming, falls, <c>holding_tool</c>) and the held tool's
    ///     <c>holding_&lt;tool&gt;</c> and <c>using_tool_first</c> flags, and a cinematic's own <c>cinematics_enabled</c> and
    ///     <c>using_tool_first</c> (upstream #2859, COOP_FEATURES_SPEC R1b).
    ///     <para />
    ///     While a cinematic poses the body (<see cref="InCinematic" />), the partner's own states are kept but not written,
    ///     so a state change on their side (a tool put away, the surface left) can't pull the arms out of the cinematic's
    ///     pose; <see cref="ClearInCinematic" /> writes the kept ones back. They are kept rather than dropped because the
    ///     partner's game sends a state only when it changes. The downed pose (T1) holds none back: it stops the body's
    ///     animator (<see cref="DownedPose" />), and the arms must be right after a revive (a downed player's light stays drawn).
    ///     <para />
    ///     A change of <c>holding_tool</c> or of a <c>holding_&lt;tool&gt;</c> flag also points the arms and hands at the
    ///     drawn tool (<see cref="ArmsController.Reconfigure" />), as <c>ArmsController.Update</c> does for the local player.
    /// </summary>
    /// <param name="cinematic">The write belongs to the cinematic playing on the body: it always lands and isn't kept.</param>
    public void SetArmState(string parameter, bool value, bool cinematic = false)
    {
        if (!cinematic)
        {
            armStates[parameter] = value;
            if (InCinematic)
            {
                return;
            }
        }

        AnimationController[parameter] = value;

        if (parameter.StartsWith("holding_", System.StringComparison.Ordinal))
        {
            ReconfigureArms();
        }
    }

    private void RestoreArmStates()
    {
        if (!AnimationController)
        {
            return;
        }
        foreach (KeyValuePair<string, bool> armState in armStates)
        {
            AnimationController[armState.Key] = armState.Value;
        }
        ReconfigureArms();
    }

    /// <summary>
    ///     The game reconfigures the arms when the tool in hand changes, with no tool while the PDA is open
    ///     (<c>game:ArmsController.cs:456-465</c>). The partner's <c>holding_tool</c> already says that (it is off while
    ///     their PDA is open), and the drawn tool is the one plugged into the hand. <c>Reconfigure</c> needs
    ///     <c>ArmsController.Start</c> (its IK and the PDA), which runs a frame after the body is made. Its no-tool branch
    ///     reads the <i>local</i> player's PDA (<c>Start</c> takes the local player), as the world-target reconfigure in
    ///     <c>ArmsController_Update_Patch</c> already does.
    /// </summary>
    private void ReconfigureArms()
    {
        if (!ArmsController || !ArmsController.ik)
        {
            return;
        }

        PlayerTool? tool = AnimationController["holding_tool"] && ItemAttachPoint ? ItemAttachPoint.GetComponentInChildren<PlayerTool>() : null;
        if (tool == armsConfiguredFor)
        {
            return;
        }
        armsConfiguredFor = tool;
        ArmsController.Reconfigure(tool);
    }

    /// <summary>
    ///     What the partner wears, from their player metadata (e.g. a tank, for their breathing sound).
    /// </summary>
    public IReadOnlyList<TechType> Equipment { get; private set; } = [];

    public void UpdateEquipmentVisibility(List<TechType> equippedItems)
    {
        Equipment = equippedItems;
        PlayerModelManager.UpdateEquipmentVisibility(equipmentVisibilityHandlers, equippedItems);
    }

    /// <summary>
    /// Makes the RemotePlayer recognizable as an obstacle for buildings, and as a target for creatures
    /// </summary>
    private void SetupBody()
    {
        // set as a target for reapers
        EcoTarget sharkEcoTarget = Body.AddComponent<EcoTarget>();
        sharkEcoTarget.SetTargetType(EcoTargetType.Shark);

        EcoTarget playerEcoTarget = Body.AddComponent<EcoTarget>();
        playerEcoTarget.SetTargetType(PLAYER_ECO_TARGET_TYPE);

        TechTag techTag = Body.AddComponent<TechTag>();
        techTag.type = TechType.Player;

        RemotePlayerIdentifier identifier = Body.AddComponent<RemotePlayerIdentifier>();
        identifier.RemotePlayer = this;

        if (Player.mainCollider is CapsuleCollider refCollider)
        {
            // This layer lets us have a collider as a trigger without preventing its detection as an obstacle
            Body.layer = LayerID.Useable;
            Collider = Body.AddComponent<CapsuleCollider>();

            Collider.center = Vector3.zero;
            Collider.height = refCollider.height;
            Collider.radius = refCollider.radius;
            Collider.direction = refCollider.direction;
            Collider.contactOffset = refCollider.contactOffset;
            Collider.isTrigger = true;
        }
        else
        {
            Log.Warn("The main collider of the main Player couldn't be found or is not a CapsuleCollider. Collisions for the RemotePlayer won't be created");
        }

        Partner.PartnerHandTarget.AddTo(this);
    }

    /// <summary>
    /// Allows the remote player model to have its lighting dynamically adjusted
    /// </summary>
    private void SetupSkyAppliers()
    {
        // SkyAppliers apply the light effects of a lighting source on a set of renderers
        SkyApplier skyApplier = Body.AddComponent<SkyApplier>();
        skyApplier.anchorSky = Skies.Auto;
        skyApplier.emissiveFromPower = false;
        skyApplier.dynamic = true;
        skyApplier.renderers = Body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    /// <summary>
    /// Sets up all bubbles, breathing and diving sounds to be multiplayer ready
    /// </summary>
    private void SetupPlayerSounds()
    {
        GameObject remotePlayerSoundsRoot = new("RemotePlayerSounds");
        remotePlayerSoundsRoot.transform.SetParent(Body.transform);
        FMODEmitterController emitterController = Body.AddComponent<FMODEmitterController>();

        static void CopyEmitter(FMOD_CustomEmitter src, FMOD_CustomEmitter dst)
        {
            dst.asset = src.asset;
            dst.playOnAwake = src.playOnAwake;
            dst.stopImmediatelyOnDisable = src.stopImmediatelyOnDisable;
            dst.followParent = src.followParent;
            dst.restartOnPlay = src.restartOnPlay;
        }

        // Bubbles
        PlayerBreathBubbles localPlayerBubbles = Player.main.GetComponentInChildren<PlayerBreathBubbles>(true);
        FMOD_CustomEmitter bubblesCustomEmitter = remotePlayerSoundsRoot.AddComponent<FMOD_CustomEmitter>();
        CopyEmitter(localPlayerBubbles.bubbleSound, bubblesCustomEmitter);

        if (fmodWhitelist.IsWhitelisted(bubblesCustomEmitter.asset.path, out float bubblesSoundRadius))
        {
            emitterController.AddEmitter(bubblesCustomEmitter.asset.path, bubblesCustomEmitter, bubblesSoundRadius);
        }
        else
        {
            Log.Error($"[{nameof(RemotePlayer)}] Manual created FMOD emitter for {nameof(PlayerBreathBubbles)} but linked sound is not whitelisted: ({bubblesCustomEmitter.asset.path})");
        }

        // Breathing
        BreathingSound breathingSound = Player.main.GetComponentInChildren<BreathingSound>(true);
        FMOD_CustomEmitter breathingSoundCustomEmitter = remotePlayerSoundsRoot.AddComponent<FMOD_CustomEmitter>();
        breathingSoundCustomEmitter.asset = breathingSound.loopingBreathingSound.asset;

        if (fmodWhitelist.IsWhitelisted(breathingSoundCustomEmitter.asset.path, out float breathingSoundRadius))
        {
            emitterController.AddEmitter(breathingSoundCustomEmitter.asset.path, breathingSoundCustomEmitter, breathingSoundRadius);
        }
        else
        {
            Log.Error($"[{nameof(RemotePlayer)}] Manual created FMOD emitter for {nameof(BreathingSound)} but linked sound is not whitelisted: ({breathingSoundCustomEmitter.asset.path})");
        }

        // Pain and the breathing loop (COOP_FEATURES_SPEC D1): the emitter above is never played, the partner's game doesn't send it
        Body.AddComponent<RemotePlayerSounds>().Initialize(this, fmodWhitelist);
    }

    /// <summary>
    /// An InfectedMixin is required for behaviours like <see cref="AggressiveWhenSeeTarget"/> which look for this on the target they find
    /// </summary>
    private void SetupMixins()
    {
        InfectedMixin = Body.AddComponent<InfectedMixin>();
        InfectedMixin.shaderKeyWord = InfectedMixin.uwe_playerinfection;
        Renderer renderer = PlayerModel.transform.Find("male_geo/diveSuit/diveSuit_hands_geo").GetComponent<Renderer>();
        InfectedMixin.renderers = [renderer];

        LiveMixin = Body.AddComponent<LiveMixin>();
        LiveMixin.data = new()
        {
            maxHealth = 100,
            broadcastKillOnDeath = false
        };
        LiveMixin.health = 100;
        // We set the remote player to invincible because we only want this component to be detectable but not to work
        LiveMixin.invincible = true;
    }

    public void UpdateHealthAndInfection(float health, float infection)
    {
        if (LiveMixin)
        {
            LiveMixin.health = health;
        }

        if (InfectedMixin)
        {
            InfectedMixin.infectedAmount = infection;
            InfectedMixin.UpdateInfectionShading();
        }
    }

    public void SetGameMode(SubnauticaGameMode gameMode)
    {
        PlayerContext.GameMode = gameMode;
        RefreshVitalsVisibility();
    }

    private void RefreshVitalsVisibility()
    {
        if (vitals)
        {
            // TODO: only show health and oxygen in freedom mode
            bool visible = PlayerContext.GameMode != SubnauticaGameMode.CREATIVE;
            vitals.SetStatsVisible(visible);
        }
    }

    /// <summary>
    /// Adaptation of <see cref="Player.CanBeAttacked"/> for remote players.
    /// NB: This doesn't check for other player's use of 'invisible' command
    /// </summary>
    public bool CanBeAttacked()
    {
        return !SubRoot && !EscapePod && PlayerContext.GameMode != SubnauticaGameMode.CREATIVE;
    }
}
