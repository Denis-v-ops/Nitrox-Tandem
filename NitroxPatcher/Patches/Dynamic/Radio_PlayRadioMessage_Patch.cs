using System;
using System.Collections.Generic;
using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Story;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Radio_PlayRadioMessage_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Radio t) => t.PlayRadioMessage());

    /// <summary>
    ///     The radio message that starts the Sunbeam countdown: <c>StoryGoalCustomEventHandler.NotifyGoalComplete</c>
    ///     starts it on "OnPlay" + this key, which <c>StoryGoalManager.ExecutePendingRadioMessage</c> completes.
    /// </summary>
    private const string SUNBEAM_MESSAGE = "RadioSunbeam4";

    public static bool Prefix(Radio __instance)
    {
        if (IsSunbeamMessageNext() && !Resolve<StoryGates>().IsPassing(StoryGateKind.SUNBEAM_RADIO))
        {
            // COOP_FEATURES_SPEC G0 radio lock: the message plays once everyone on the roster is online. Nothing was taken
            // from the queue yet (ExecutePendingRadioMessage does that), so the message stays playable.
            Resolve<StoryGates>().Hold(StoryGateKind.SUNBEAM_RADIO, () =>
            {
                if (__instance)
                {
                    __instance.PlayRadioMessage();
                }
            });
            // Radio.OnHandClick already hid the HUD's radio icon; the message is still waiting, so show it again.
            StoryGoalManager.main.PulsePendingMessages();
            return false;
        }

        Resolve<IPacketSender>().Send(new RadioPlayPendingMessage());
        return true;
    }

    private static bool IsSunbeamMessageNext()
    {
        StoryGoalManager storyGoalManager = StoryGoalManager.main;
        if (!storyGoalManager)
        {
            return false;
        }
        List<string> pending = storyGoalManager.pendingRadioMessages;
        return pending.Count > 0 && string.Equals(pending[0], SUNBEAM_MESSAGE, StringComparison.OrdinalIgnoreCase);
    }
}
