using System.Collections;
using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours.Gui.HUD;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

// The incubator class is attached to the main terminal window.  This is what the player clicks
// to hatch the eggs using the enzymes.
public sealed partial class Incubator_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Incubator t) => t.OnHandClick(default(GUIHand)));

    /// <summary>How long a player who confirmed the hatching waits for the carrier's hatch to arrive before giving up on the music.</summary>
    private const float HATCH_WAIT_SECONDS = 20f;

    private static bool skipPrefix;

    public static bool Prefix(Incubator __instance, GUIHand hand)
    {
        if (skipPrefix)
        {
            return true;
        }

        // Only an incubator the game would hatch now (Incubator.terminalActive: powered and not hatched yet)
        if (!__instance.powered || __instance.hatched)
        {
            return false;
        }

        // COOP_FEATURES_SPEC G3b: the eggs hatch once both players have used the incubator. The player carrying the
        // enzymes hatches them; the partner's click confirms.
        StoryGates storyGates = Resolve<StoryGates>();
        if (!storyGates.IsPassing(StoryGateKind.HATCHING))
        {
            storyGates.Hold(StoryGateKind.HATCHING, () => __instance.OnHandClick(hand));
            return false;
        }

        if (!Inventory.main.container.Contains(TechType.HatchingEnzymes))
        {
            __instance.StartCoroutine(PlayHatchingSoundsOnceHatched(__instance));
            return false;
        }

        // The server only knows about the the main incubator platform which is the direct parent
        GameObject platform = __instance.transform.parent.gameObject;

        // Request a simulation lock on the incubator so that we can authoritatively spawn the resulting creatures
        if (platform.TryGetIdOrWarn(out NitroxId id))
        {
            HandInteraction<Incubator> context = new(__instance, hand);
            LockRequest<HandInteraction<Incubator>> lockRequest = new(id, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, context);

            Resolve<SimulationOwnership>().RequestSimulationLock(lockRequest);
        }

        return false;
    }

    private static void ReceivedSimulationLockResponse(NitroxId id, bool lockAquired, HandInteraction<Incubator> context)
    {
        if (lockAquired)
        {
            IncubatorMetadata metadata = new(true, true);

            Entities entities = NitroxServiceLocator.LocateService<Entities>();
            entities.BroadcastMetadataUpdate(id, metadata);

            skipPrefix = true;
            context.Target.OnHandClick(context.GuiHand);
            skipPrefix = false;
        }
        else
        {
            context.Target.gameObject.AddComponent<DenyOwnershipHand>();
            // Both carried enzymes and the other player's game hatches them: hear it like a partner who confirmed.
            context.Target.StartCoroutine(PlayHatchingSoundsOnceHatched(context.Target));
        }
    }

    /// <summary>
    ///     G3b: the game plays the hatching soundscape and music only for the player who inserts the enzymes
    ///     (<c>game:Incubator.cs</c> 140-141); the partner's game hatches through <c>IncubatorMetadataProcessor</c>, which
    ///     plays neither. A partner who confirmed hears them as soon as the hatch arrives.
    /// </summary>
    private static IEnumerator PlayHatchingSoundsOnceHatched(Incubator incubator)
    {
        float giveUpAt = Time.time + HATCH_WAIT_SECONDS;
        while (incubator && !incubator.hatched && Time.time < giveUpAt)
        {
            yield return null;
        }
        if (incubator && incubator.hatched)
        {
            Utils.PlayFMODAsset(incubator.hatchingSoundscape, incubator.transform, 30f);
            Utils.PlayFMODAsset(incubator.hatchingMusic);
        }
    }
}
