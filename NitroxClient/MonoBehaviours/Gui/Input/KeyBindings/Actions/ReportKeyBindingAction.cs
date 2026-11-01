using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
///     Saves a bug report without typing <c>/report</c>. F4 is free: the game binds F8 and F11, checks F1-F3, F5-F8 and F10
///     directly, and Nitrox's debugger uses F7 and F9.
/// </summary>
internal sealed class ReportKeyBindingAction : KeyBinding
{
    public ReportKeyBindingAction() : base("Nitrox_Settings_Keybind_Report", "f4") { }

    public override void Execute(InputAction.CallbackContext _)
    {
        if (Multiplayer.Joined)
        {
            Multiplayer.Main.CreateBugReport("");
        }
    }
}
