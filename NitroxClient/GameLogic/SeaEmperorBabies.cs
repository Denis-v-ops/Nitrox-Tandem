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
    ///     How long a copy waits hidden at most before it swims anyway (normally it goes when the simulating game's baby
    ///     leaves its egg, see <see cref="HasLeft" />).
    /// </summary>
    private const float HATCH_WAIT_SECONDS = 60f;

    /// <summary>
    ///     Speed (m/s) of the simulating game's baby from which it counts as swimming: a baby in a cinematic (in its egg, at
    ///     its mother) is kinematic and sends 0; one that swims to its mother or to the teleporter does about 4.
    /// </summary>
    private const float SWIMMING_SPEED = 1f;

    /// <summary>
    ///     A copy's visit at its mother lasts at least this long (s) before the simulating game's baby can end it: the last
    ///     pose before the visit may still be one of the baby swimming in.
    /// </summary>
    private const float MIN_VISIT_SECONDS = 10f;

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

    /// <summary>
    ///     The copy waits hidden until the hatching of its egg ended here, or the simulating game's baby left its egg. The egg
    ///     and baby animators cull completely: a game whose player doesn't look at the eggs doesn't play the hatching (seen
    ///     windowed 2026-11-03: two eggs still hatching after 90 s), while the simulating game's baby is already on its way.
    ///     Then this game's hatching ends with it (the temporary baby goes, IncubatorEggAnimation_OnHatchAnimationEnd_Patch).
    /// </summary>
    private static IEnumerator SwimOnceHatched(SeaEmperorBaby baby, IncubatorEgg egg)
    {
        List<Renderer> hidden = [.. baby.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled)];
        hidden.ForEach(renderer => renderer.enabled = false);
        float giveUpAt = Time.time + HATCH_WAIT_SECONDS;
        while (egg && IsHatchingLocally(egg) && !HasLeft(baby) && Time.time < giveUpAt)
        {
            yield return null;
        }
        string why = !egg || !IsHatchingLocally(egg) ? "the local hatching ended" : HasLeft(baby) ? "its baby left the egg in the simulating game" : "gave up waiting";
        if (egg && IsHatchingLocally(egg))
        {
            egg.CancelInvoke(nameof(IncubatorEgg.HatchNow));
            if (egg.animationController && egg.animationController.animationActive)
            {
                egg.animationController.OnHatchAnimationEnd();
            }
        }
        hidden.Where(renderer => renderer).ToList().ForEach(renderer => renderer.enabled = true);
        if (!baby)
        {
            yield break;
        }
        Log.Info($"[{nameof(SeaEmperorBabies)}] Baby {baby.GetId()} ({Describe(baby)}) swims to its mother: {why}");
        baby.SwimToMother();
    }

    /// <summary>
    ///     Ends a copy's visit at its mother when the simulating game's baby has left (it swims to the teleporter), as the
    ///     visit's animation event would (<c>SeaEmperorBabyCinematicController.OnCinematicModeEnd</c> → <c>SwimToTeleporter</c>
    ///     → <c>DropEnzymes</c>). That event doesn't come while this player doesn't look at the babies (the animator culls
    ///     completely), and the simulating game's teleport removes the copy.
    /// </summary>
    public static IEnumerator EndVisitWithSimulatingGame(SeaEmperorBaby baby, RemotelyControlled remotelyControlled)
    {
        float canEndAt = Time.time + MIN_VISIT_SECONDS;
        // The baby is disabled while its cinematic plays (SeaEmperorBabyCinematicController.SetCinematicMode)
        while (baby && !baby.enabled)
        {
            if (Time.time >= canEndAt && HasLeft(remotelyControlled))
            {
                Log.Info($"[{nameof(SeaEmperorBabies)}] Baby {baby.GetId()} ({Describe(baby)}) ends its visit: it left in the simulating game");
                baby.cinematicController.OnCinematicModeEnd();
                yield break;
            }
            yield return null;
        }
    }

    private static bool HasLeft(SeaEmperorBaby baby) => baby && baby.TryGetComponent(out RemotelyControlled remotelyControlled) && HasLeft(remotelyControlled);

    private static bool HasLeft(RemotelyControlled remotelyControlled) =>
        remotelyControlled && remotelyControlled.HasCreatureMovement && remotelyControlled.CreatureVelocity.magnitude > SWIMMING_SPEED;

    private static string Describe(SeaEmperorBaby baby)
    {
        return baby.TryGetNitroxId(out NitroxId id) ? id.ToString() : baby.name;
    }
}
