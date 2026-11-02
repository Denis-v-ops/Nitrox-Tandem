#if DEBUG
using System.Reflection;
using NitroxClient.Debuggers.Bridge;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Dev bridge <c>hold</c> (<see cref="BridgeHold" />): adds the held button's Down/Held/Up flags to the game's input.
///     Patched on the input implementation, not on <c>GameInput.GetButtonHeld</c>, which is small enough to be inlined.
///     <c>GameInput.HasFlag</c> checks <c>clearInput</c> before calling this, so cleared input stays cleared.
/// </summary>
public sealed partial class GameInputSystem_GetButtonState_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((GameInputSystem t) => t.GetButtonState(default));

    public static void Postfix(GameInput.Button action, ref GameInput.InputStateFlags __result)
    {
        __result |= BridgeHold.GetFlags(action);
    }
}
#endif
