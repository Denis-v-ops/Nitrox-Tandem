using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
///     "Ping: come here" (COOP_FEATURES_SPEC F2): a "come here" ping wherever the crosshair hits, even on a creature or an
///     item. H is free: the game binds no letter key to it, and Nitrox uses Y, I and F4.
/// </summary>
internal sealed class ComeHerePingKeyBindingAction : KeyBinding
{
    public ComeHerePingKeyBindingAction() : base("Nitrox_Settings_Keybind_PingComeHere", "h") { }

    public override void Execute(InputAction.CallbackContext _)
    {
        PlayerPingManager.RequestComeHerePing();
    }
}
