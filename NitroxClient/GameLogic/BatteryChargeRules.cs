using System;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: when a battery's charge is sent to the other players. Plain data, no game types.
/// </summary>
public static class BatteryChargeRules
{
    /// <summary>
    ///     A battery changes its charge every frame; it is sent when the whole number changes and when it runs empty.
    /// </summary>
    /// <param name="oldCharge">The charge before the change.</param>
    /// <param name="newCharge">The charge after the change, already limited to the capacity as the game's setter does.</param>
    public static bool ShouldSend(float oldCharge, float newCharge)
    {
        bool wholeNumberChanged = Math.Floor(oldCharge) != Math.Floor(newCharge);
        // Battery.IsDepleted is "charge == 0": the step that empties the battery must arrive even within the last whole number
        bool becameEmpty = newCharge <= 0f && oldCharge > 0f;
        return wholeNumberChanged || becameEmpty;
    }

    /// <summary>
    ///     The value <c>Battery.charge</c>'s setter stores (<c>Mathf.Min(value, _capacity)</c>, no lower limit).
    /// </summary>
    public static float StoredCharge(float value, float capacity) => Math.Min(value, capacity);
}
