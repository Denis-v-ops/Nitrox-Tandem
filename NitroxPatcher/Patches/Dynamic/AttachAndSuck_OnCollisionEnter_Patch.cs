using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     M4 (FIX_SPEC S15d): the contact that starts a Bleeder's attack runs on every game's copy of the Bleeder. The game
///     decides whether it is valid (target free, 4 s since the last attack, player not in a sub, attackable); when it
///     accepted the local player, that player's game asks for the Bleeder's exclusive lock, so the attack (a creature action,
///     which only the simulating game runs) runs there (<see cref="BleederClaims" />).
/// </summary>
public sealed partial class AttachAndSuck_OnCollisionEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.OnCollisionEnter(default));

    public static void Prefix(AttachAndSuck __instance, out BleederAttachTarget __state)
    {
        __state = __instance.currentTarget;
    }

    public static void Postfix(AttachAndSuck __instance, BleederAttachTarget __state)
    {
        // The game refused the contact, or it is the target it already has
        if (!__instance.currentTarget || __instance.currentTarget == __state)
        {
            return;
        }

        // A Bleeder the server doesn't know (a local-only spawn) keeps the game's behaviour: nobody else could simulate it
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            BleederClaims.Claim(__instance, id, Resolve<SimulationOwnership>());
        }
    }
}
