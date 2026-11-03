using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC P1: a follow/stay command or the farewell (the right hand button while looking at the cuddlefish)
///     first takes its simulation lock as exclusive, as playing with it does (see <see cref="Cuddlefish" />). Once the lock
///     is granted, the command starts as <see cref="CuteFishHandTarget.OnHandHover" /> would have started it.
/// </summary>
public sealed partial class CuteFishHandTarget_OnHandHover_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CuteFishHandTarget t) => t.OnHandHover(default(GUIHand)));

    public static bool Prefix(CuteFishHandTarget __instance)
    {
        if (!__instance.AllowedToInteract() || __instance.cuteFish.goodbyePlayed || !GameInput.GetButtonDown(GameInput.Button.RightHand))
        {
            return true;
        }

        return Resolve<Cuddlefish>().HasExclusiveLockOrRequest(__instance, () => StartCommand(__instance));
    }

    /// <summary>
    ///     The right hand branch of <see cref="CuteFishHandTarget.OnHandHover" />; the button press itself is frames old by now.
    /// </summary>
    private static void StartCommand(CuteFishHandTarget handTarget)
    {
        if (!handTarget.AllowedToInteract() || handTarget.cuteFish.goodbyePlayed)
        {
            return;
        }

        if (Rocket.IsAnyRocketReady || handTarget.debugGoodbye)
        {
            handTarget.PrepareCinematicMode(Player.main, handTarget.goodbyeCinematic);
            handTarget.cuteFish.goodbyePlayed = true;
        }
        else
        {
            handTarget.state = CuteFishHandTarget.State.Command;
            handTarget.commandAnimationStared = false;
        }
    }
}
