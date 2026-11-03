using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     COOP_FEATURES_SPEC G3a: the Sea Emperor comes to the aquarium platform only once both players stand on it. The
///     platform's trigger (<see cref="PrecursorAquariumPlatformTrigger" />) is held back by a patch, which adds this
///     component. While the local player stands inside the trigger's box it renews the gate's ready (the server checks
///     that everyone on the roster is inside the same box), and withdraws it when the player leaves. The game fires
///     <c>OnTriggerEnter</c> only once while the player stays inside, so the start drives the platform itself
///     (<see cref="StartPlatform" />) instead of waiting for the trigger.
/// </summary>
public class SeaEmperorPlatformGate : MonoBehaviour
{
    /// <summary>The goal of the Emperor's first words on the platform (<c>SeaEmperor.onDialog1Goal</c>, read in game 2026-11-03).</summary>
    public const string FIRST_DIALOG_GOAL = "Precursor_Prison_Aquarium_EmperorLog1";

    private const float CHECK_SECONDS = 0.5f;

    private PrecursorAquariumPlatformTrigger trigger;
    private BoxCollider volume;
    private StoryGates storyGates;
    private float nextCheck;

    /// <summary>Starts watching the local player on this platform (called when the player enters its trigger).</summary>
    public static void Watch(PrecursorAquariumPlatformTrigger trigger, StoryGates storyGates)
    {
        if (!trigger.TryGetComponent(out SeaEmperorPlatformGate gate))
        {
            gate = trigger.gameObject.AddComponent<SeaEmperorPlatformGate>();
        }
        gate.trigger = trigger;
        gate.volume = trigger.GetComponent<BoxCollider>();
        gate.storyGates = storyGates;
        gate.nextCheck = 0;
        gate.enabled = true;
    }

    /// <summary>
    ///     What <c>PrecursorAquariumPlatformTrigger.OnTriggerEnter</c> does once the local player is on the platform
    ///     (<c>game:PrecursorAquariumPlatformTrigger.cs</c> 46-58): the Emperor's and the platform's animators get the
    ///     platform parameter, the invisible walls drop after their delay and the parameter is cleared after the longest stay.
    /// </summary>
    public static void StartPlatform(PrecursorAquariumPlatformTrigger trigger)
    {
        if (!trigger || trigger.triggered)
        {
            return;
        }
        trigger.triggered = true;
        foreach (Animator animator in trigger.targetAnimators)
        {
            SafeAnimator.SetBool(animator, trigger.animationParameter, true);
        }
        trigger.Invoke(nameof(PrecursorAquariumPlatformTrigger.UpdateWalls), trigger.invisibleWallsDuration);
        trigger.Invoke(nameof(PrecursorAquariumPlatformTrigger.EndTrigger), trigger.maxStayDuration);
    }

    private void Update()
    {
        if (Time.time < nextCheck)
        {
            return;
        }
        nextCheck = Time.time + CHECK_SECONDS;
        if (!trigger || trigger.triggered || !volume || !Player.main)
        {
            enabled = false;
            return;
        }

        if (IsInside(Player.main.transform.position))
        {
            PrecursorAquariumPlatformTrigger platform = trigger;
            storyGates.Hold(StoryGateKind.SEA_EMPEROR, () => StartPlatform(platform), false);
        }
        else if (storyGates.IsHeld(StoryGateKind.SEA_EMPEROR))
        {
            storyGates.Cancel(StoryGateKind.SEA_EMPEROR);
        }
    }

    private void OnDisable()
    {
        // The platform unloaded or started: nothing to wait for any more.
        if (storyGates != null && storyGates.IsHeld(StoryGateKind.SEA_EMPEROR))
        {
            storyGates.Cancel(StoryGateKind.SEA_EMPEROR);
        }
    }

    /// <summary>Inside the trigger's (turned) box, by the player's position as the server sees it.</summary>
    private bool IsInside(Vector3 position)
    {
        Vector3 local = volume.transform.InverseTransformPoint(position) - volume.center;
        Vector3 half = volume.size / 2f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }
}
