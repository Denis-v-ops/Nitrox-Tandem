using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     The Sea Emperor's babies on a game that doesn't simulate them (FIX_SPEC S17, COOP_FEATURES_SPEC G3c). The simulating
///     game hatches the five networked babies in its eggs (<c>IncubatorEgg_HatchNow_Patch</c>) and its egg animation sends
///     each to its mother. Every other game gets them as plain world entities under a cell root, plays the hatching with a
///     temporary baby per egg, and has to send the networked copies to the mother itself: only then does its own Emperor
///     play the babies' visit, and only then do its babies leave and drop cure balls for its player
///     (<c>SeaEmperorBabyCinematicController.OnCinematicModeEnd</c> → <c>SwimToTeleporter</c> → <c>DropEnzymes</c>). Where
///     the babies are still comes from the simulating game (<see cref="RemotelyControlled" />).
/// </summary>
public static class SeaEmperorBabies
{
    /// <summary>A copy further than this (m) from every egg's attach point isn't matched to an egg (e.g. one loaded later).</summary>
    private const float EGG_MATCH_DISTANCE = 3f;

    /// <summary>
    ///     How long a copy waits hidden for the local hatching of its egg to end before it swims anyway (the egg animation's
    ///     end is an animation event, which windowless games don't fire).
    /// </summary>
    private const float HATCH_WAIT_SECONDS = 60f;

    /// <summary>A baby that is still in its egg: the egg animation (<c>IncubatorEggAnimation.OnHatchAnimationEnd</c>) sends it to its mother.</summary>
    public static bool IsInEgg(SeaEmperorBaby baby)
    {
        return baby.GetComponentInParent<IncubatorEgg>() || Object.FindObjectsOfType<IncubatorEgg>().Any(egg => egg.babyGO == baby.gameObject);
    }

    /// <summary>
    ///     Sends a networked baby that this game didn't hatch to its mother, with the id the simulating game gave it (its egg's
    ///     index, which picks its place at the mother, its part of the visit and when it leaves). While this game still plays
    ///     the hatching of that egg with its temporary baby, the copy stays hidden (it is in the same egg, a few seconds
    ///     apart) and swims once the hatching ended.
    /// </summary>
    public static void SendCopyToMother(SeaEmperorBaby baby)
    {
        if (!SeaEmperor.main)
        {
            return;
        }
        (IncubatorEgg egg, int index) = FindEgg(baby.transform.position);
        int id = index >= 0 ? index : FreeId();
        baby.SetId(id);
        if (egg && IsHatchingLocally(egg))
        {
            baby.StartCoroutine(SwimOnceHatched(baby, egg));
            return;
        }
        Log.Info($"[{nameof(SeaEmperorBabies)}] Baby {id} ({Describe(baby)}) swims to its mother");
        baby.SwimToMother();
    }

    /// <summary>The egg whose attach point is nearest to <paramref name="position" /> (within <see cref="EGG_MATCH_DISTANCE" />), and its index in its incubator.</summary>
    private static (IncubatorEgg egg, int index) FindEgg(Vector3 position)
    {
        IncubatorEgg nearest = null;
        int nearestIndex = -1;
        float nearestDistance = EGG_MATCH_DISTANCE;
        foreach (Incubator incubator in Object.FindObjectsOfType<Incubator>())
        {
            for (int i = 0; i < incubator.eggs.Count; i++)
            {
                IncubatorEgg egg = incubator.eggs[i];
                if (!egg || !egg.attachPoint)
                {
                    continue;
                }
                float distance = Vector3.Distance(egg.attachPoint.position, position);
                if (distance < nearestDistance)
                {
                    nearest = egg;
                    nearestIndex = i;
                    nearestDistance = distance;
                }
            }
        }
        return (nearest, nearestIndex);
    }

    /// <summary>The lowest id no baby at the mother has yet (a copy that isn't near any egg, e.g. after a restart).</summary>
    private static int FreeId()
    {
        HashSet<int> used = [.. SeaEmperor.main.GetBabies().Where(baby => baby).Select(baby => baby.GetId())];
        int id = 0;
        while (used.Contains(id))
        {
            id++;
        }
        return id;
    }

    /// <summary>The egg's hatch is scheduled (<c>IncubatorEgg.StartHatch</c>) or its animation plays.</summary>
    private static bool IsHatchingLocally(IncubatorEgg egg)
    {
        return egg.IsInvoking(nameof(IncubatorEgg.HatchNow)) || (egg.animationController && egg.animationController.animationActive);
    }

    private static IEnumerator SwimOnceHatched(SeaEmperorBaby baby, IncubatorEgg egg)
    {
        List<Renderer> hidden = [.. baby.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled)];
        hidden.ForEach(renderer => renderer.enabled = false);
        float giveUpAt = Time.time + HATCH_WAIT_SECONDS;
        while (egg && IsHatchingLocally(egg) && Time.time < giveUpAt)
        {
            yield return null;
        }
        hidden.Where(renderer => renderer).ToList().ForEach(renderer => renderer.enabled = true);
        Log.Info($"[{nameof(SeaEmperorBabies)}] Baby {baby.GetId()} ({Describe(baby)}) swims to its mother after the local hatching{(Time.time >= giveUpAt ? " (gave up waiting)" : "")}");
        baby.SwimToMother();
    }

    private static string Describe(SeaEmperorBaby baby)
    {
        return baby.TryGetNitroxId(out NitroxId id) ? id.ToString() : baby.name;
    }
}
