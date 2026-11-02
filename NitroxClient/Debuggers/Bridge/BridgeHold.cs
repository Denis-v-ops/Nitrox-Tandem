#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     <c>hold &lt;target|none&gt; &lt;seconds&gt; [button] [noaim]</c>: holds a game button (default <c>LeftHand</c>, the
///     interact button) for some seconds, as if a player pressed it: <c>Down</c> and <c>Held</c> on the first frame,
///     <c>Held</c> while it lasts, <c>Up</c> on the last frame. It goes through the game's own input
///     (<c>GameInputSystem.GetButtonState</c>, see <c>GameInputSystem_GetButtonState_Patch</c>), so
///     <c>GameInput.GetButtonDown/Held/Up</c>, <see cref="GUIHand" /> (hover, click) and tools see it, and hand targets
///     that drive <c>HandReticle.SetProgress</c> from <c>GetButtonHeld</c> fill their progress.
///     Unless <c>noaim</c>, the camera is turned toward the target every frame, so <see cref="GUIHand" />'s own raycast
///     (2 m) picks it; <c>hold status</c> reports which object it targeted and the reticle progress.
/// </summary>
public static class BridgeHold
{
    private static Hold? current;
    private static Hold? last;
    private static bool subscribed;

    /// <summary>
    ///     The flags the held button adds this frame, read by the GetButtonState patch.
    /// </summary>
    public static GameInput.InputStateFlags GetFlags(GameInput.Button button)
    {
        // Readers that run before the UpdateBeforeInput queue in a frame (e.g. LeftHandButton, the helm screen buttons,
        // from BehaviourUpdateManager.Update) would otherwise never see the first frame's Down.
        BeforeInput();
        Hold? hold = current ?? last;
        if (hold == null || hold.Button != button || hold.FlagsFrame != Time.frameCount)
        {
            return GameInput.InputStateFlags.None;
        }
        return hold.Flags;
    }

    public static object Command(List<string> args)
    {
        const string USAGE = "hold <target|none> <seconds> [button] [noaim] | hold status | hold off";
        if (args.Count == 0)
        {
            throw new ArgumentException(USAGE);
        }
        switch (args[0].ToLowerInvariant())
        {
            case "status":
                return Status(current ?? last);
            case "off":
                if (current != null)
                {
                    current.EndAt = Time.time;
                }
                return Status(current ?? last);
        }
        if (args.Count < 2)
        {
            throw new ArgumentException(USAGE);
        }
        if (!Player.main || !Player.main.guiHand)
        {
            throw new InvalidOperationException("No local player (not in game yet)");
        }
        if (current != null)
        {
            throw new InvalidOperationException("Already holding; wait for it or send 'hold off'");
        }
        GameObject? target = args[0].Equals("none", StringComparison.OrdinalIgnoreCase) ? null : BridgeReflection.ResolveTarget(args[0]);
        float seconds = float.Parse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture);
        GameInput.Button button = GameInput.Button.LeftHand;
        bool aim = target;
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i].Equals("noaim", StringComparison.OrdinalIgnoreCase))
            {
                aim = false;
            }
            else
            {
                button = (GameInput.Button)Enum.Parse(typeof(GameInput.Button), args[i], true);
            }
        }
        if (GameInput.input is not GameInputSystem)
        {
            throw new InvalidOperationException($"hold needs GameInputSystem, this game uses {GameInput.input?.GetType().Name ?? "no input"}");
        }
        current = new Hold(button, seconds, target, aim);
        last = null;
        if (!subscribed)
        {
            ManagedUpdate.Subscribe(ManagedUpdate.Queue.UpdateBeforeInput, BeforeInput);
            ManagedUpdate.Subscribe(ManagedUpdate.Queue.UpdateAfterInput, AfterInput);
            subscribed = true;
        }
        return Status(current);
    }

    private static void BeforeInput()
    {
        Hold? hold = current;
        if (hold == null || hold.FlagsFrame == Time.frameCount || Time.frameCount < hold.FirstFrame)
        {
            return;
        }
        hold.FlagsFrame = Time.frameCount;
        if (!hold.Started)
        {
            hold.Started = true;
            hold.StartTime = Time.time;
            hold.EndAt ??= Time.time + hold.Seconds;
            hold.Flags = GameInput.InputStateFlags.Down | GameInput.InputStateFlags.Held;
        }
        else if (Time.time >= hold.EndAt)
        {
            hold.Flags = GameInput.InputStateFlags.Up;
            hold.Finished = true;
        }
        else
        {
            hold.Flags = GameInput.InputStateFlags.Held;
        }
        if (hold.Aim && hold.Target)
        {
            AimAt(hold);
        }
    }

    private static void AfterInput()
    {
        Hold? hold = current;
        if (hold == null || hold.FlagsFrame != Time.frameCount)
        {
            return;
        }
        hold.Frames++;
        GameObject? activeTarget = Player.main && Player.main.guiHand ? Player.main.guiHand.GetActiveTarget() : null;
        hold.LastActiveTarget = activeTarget ? activeTarget!.name : null;
        if (hold.Target && activeTarget && (activeTarget == hold.Target || activeTarget!.transform.IsChildOf(hold.Target!.transform) || hold.Target!.transform.IsChildOf(activeTarget.transform)))
        {
            hold.TargetedFrames++;
        }
        if (HandReticle.main)
        {
            hold.LastProgress = HandReticle.main.progress;
            hold.MaxProgress = Math.Max(hold.MaxProgress, hold.LastProgress);
        }
        if (hold.Finished)
        {
            last = hold;
            current = null;
            // The Up flags stay readable for the rest of this frame through "last".
        }
    }

    private static void AimAt(Hold hold)
    {
        MainCameraControl cameraControl = MainCameraControl.main;
        if (!cameraControl || !MainCamera.camera || !cameraControl.transform.parent)
        {
            return;
        }
        Vector3 point = TargetPoint(hold.Target!);
        Vector3 direction = point - MainCamera.camera.transform.position;
        hold.Distance = direction.magnitude;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }
        Vector3 local = cameraControl.transform.parent.InverseTransformDirection(direction.normalized);
        cameraControl.rotationX = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        cameraControl.rotationY = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, cameraControl.minimumY, cameraControl.maximumY);
    }

    /// <summary>
    ///     Center of what GUIHand's raycast can hit: the target's collider (triggers included, like the partner's body), else
    ///     its renderers, else its position.
    /// </summary>
    private static Vector3 TargetPoint(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (!collider)
        {
            collider = target.GetComponentInChildren<Collider>();
        }
        if (collider && collider.enabled)
        {
            return collider.bounds.center;
        }
        Renderer renderer = target.GetComponentInChildren<Renderer>();
        return renderer ? renderer.bounds.center : target.transform.position;
    }

    private static Dictionary<string, object?> Status(Hold? hold)
    {
        if (hold == null)
        {
            return new Dictionary<string, object?> { ["active"] = false };
        }
        return new Dictionary<string, object?>
        {
            ["active"] = ReferenceEquals(hold, current),
            ["button"] = hold.Button.ToString(),
            ["seconds"] = hold.Seconds,
            ["target"] = hold.Target ? BridgeReflection.Summary(hold.Target!) : null,
            ["aim"] = hold.Aim,
            ["elapsed"] = hold.Started ? Math.Round(Time.time - hold.StartTime, 2) : 0,
            ["frames"] = hold.Frames,
            ["targetedFrames"] = hold.TargetedFrames,
            ["lastActiveTarget"] = hold.LastActiveTarget,
            ["distance"] = Math.Round(hold.Distance, 2),
            ["lastProgress"] = Math.Round(hold.LastProgress, 3),
            ["maxProgress"] = Math.Round(hold.MaxProgress, 3),
            // GUIHand only hovers and clicks while this is true (no menu open, cursor hidden).
            ["avatarInputEnabled"] = AvatarInputHandler.main && AvatarInputHandler.main.IsEnabled()
        };
    }

    private sealed class Hold(GameInput.Button button, float seconds, GameObject? target, bool aim)
    {
        public GameInput.Button Button { get; } = button;
        public float Seconds { get; } = seconds;
        public GameObject? Target { get; } = target;
        public bool Aim { get; } = aim;
        public bool Started;
        public bool Finished;
        public float StartTime;
        public float? EndAt;
        public int FlagsFrame = -1;
        // The hold starts on the frame after the command: readers earlier in the command's frame have already run, so
        // a Down in that frame would reach only some of them.
        public int FirstFrame = Time.frameCount + 1;
        public GameInput.InputStateFlags Flags;
        public int Frames;
        public int TargetedFrames;
        public string? LastActiveTarget;
        public float Distance;
        public float LastProgress;
        public float MaxProgress;
    }
}
#endif
