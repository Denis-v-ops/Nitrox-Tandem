using System;
using System.Collections.Generic;
using Nitrox.Model.GameLogic.PlayerAnimation;

namespace NitroxClient.GameLogic.PlayerLogic;

/// <summary>
///     The arm and body states of a player's own animator that their partner's game shows on their body (COOP_FEATURES_SPEC
///     R2b). The game writes them on the local body only: <c>ArmsController.Update</c> (tool use, <c>holding_tool</c>,
///     <c>jump</c>, <c>diving</c>, <c>diving_land</c>), <c>ConditionRules</c> (<c>on_surface</c>), the PDA
///     (<c>using_pda</c>) and <c>ArmsController.SetUsingBuilder</c>. On the partner's body <c>ArmsController.Update</c>
///     is skipped and <c>ConditionRules</c> is off, so nothing sets them there.
/// </summary>
public static class ArmStates
{
    /// <summary>
    ///     Each synced type and the animator parameter it stands for.
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<AnimChangeType, string>> Parameters =
    [
        new(AnimChangeType.USING_TOOL, "using_tool"),
        new(AnimChangeType.USING_TOOL_ALT, "using_tool_alt"),
        new(AnimChangeType.USING_BUILDER, "using_builder"),
        new(AnimChangeType.USING_PDA, "using_pda"),
        new(AnimChangeType.ON_SURFACE, "on_surface"),
        new(AnimChangeType.JUMP, "jump"),
        new(AnimChangeType.DIVING, "diving"),
        new(AnimChangeType.DIVING_LAND, "diving_land"),
        new(AnimChangeType.HOLDING_TOOL, "holding_tool")
    ];

    public static bool TryGetParameter(AnimChangeType type, out string parameter)
    {
        foreach (KeyValuePair<AnimChangeType, string> entry in Parameters)
        {
            if (entry.Key == type)
            {
                parameter = entry.Value;
                return true;
            }
        }
        parameter = "";
        return false;
    }
}

/// <summary>
///     Finds the changes to send from the local player's arm states (<see cref="ArmStates" />), sampled once a frame.
///     Every state is sent once at the start. A state that turns on is sent at once; one that turns off is sent only after
///     it has stayed off for <see cref="OFF_DELAY_SECONDS" />. A click tool sets <c>using_tool</c> for a single frame
///     (<c>GUIHand.usedToolThisFrame</c> is set on the button's down frame only), so without the delay a click would be an
///     on and an off packet that often arrive in the same frame of the partner's game, and the animator there would never
///     see the on; a state that flickers would be sent every frame.
/// </summary>
public sealed class ArmStateWatch
{
    public const float OFF_DELAY_SECONDS = 0.15f;

    private readonly AnimChangeState[] sent;
    private readonly float[] offSince;

    public ArmStateWatch()
    {
        sent = new AnimChangeState[ArmStates.Parameters.Count];
        offSince = new float[ArmStates.Parameters.Count];
        for (int i = 0; i < sent.Length; i++)
        {
            sent[i] = AnimChangeState.UNSET;
            offSince[i] = -1;
        }
    }

    /// <param name="isOn">The local animator's value of a parameter.</param>
    /// <param name="time">The current time in seconds.</param>
    /// <returns>The states to send now, in <see cref="ArmStates.Parameters" /> order.</returns>
    public List<KeyValuePair<AnimChangeType, AnimChangeState>> Update(Func<string, bool> isOn, float time)
    {
        List<KeyValuePair<AnimChangeType, AnimChangeState>> changes = [];
        for (int i = 0; i < sent.Length; i++)
        {
            KeyValuePair<AnimChangeType, string> entry = ArmStates.Parameters[i];
            bool on = isOn(entry.Value);
            if (on)
            {
                offSince[i] = -1;
                if (sent[i] != AnimChangeState.ON)
                {
                    sent[i] = AnimChangeState.ON;
                    changes.Add(new(entry.Key, AnimChangeState.ON));
                }
                continue;
            }

            if (sent[i] == AnimChangeState.OFF)
            {
                continue;
            }
            if (sent[i] == AnimChangeState.UNSET)
            {
                sent[i] = AnimChangeState.OFF;
                changes.Add(new(entry.Key, AnimChangeState.OFF));
                continue;
            }
            if (offSince[i] < 0)
            {
                offSince[i] = time;
            }
            if (time - offSince[i] >= OFF_DELAY_SECONDS)
            {
                offSince[i] = -1;
                sent[i] = AnimChangeState.OFF;
                changes.Add(new(entry.Key, AnimChangeState.OFF));
            }
        }
        return changes;
    }
}
