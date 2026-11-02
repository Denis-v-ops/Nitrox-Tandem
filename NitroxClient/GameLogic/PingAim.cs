using System;
using System.Collections.Generic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     The ray of a ping (MULTINAUGHTICA_SPEC M9): through the crosshair, past the player's own body, the vehicle they sit
///     in and, at the helm, the Cyclops they steer, so a ping from a seat reaches what is outside; from half a metre on;
///     and onto a partner's body, which is a trigger. <see cref="PingAimRules" /> decides which hit wins.
/// </summary>
internal static class PingAim
{
    private static readonly RaycastHit[] solidHits = new RaycastHit[64];
    private static readonly RaycastHit[] useableHits = new RaycastHit[16];
    private static readonly List<RaycastHit> hits = [];
    private static readonly List<PingAimRules.Hit> candidates = [];

    /// <summary>The nearest thing the crosshair points at that a ping can land on, or null.</summary>
    public static RaycastHit? FromCrosshair(float maxDistance)
    {
        return TryGetCrosshairRay(out Ray ray) ? Along(ray, maxDistance) : null;
    }

    /// <summary>The same along any ray, e.g. one from the camera toward a point (the dev bridge's pings from a seat).</summary>
    public static RaycastHit? Along(Ray ray, float maxDistance)
    {
        int index = Collect(ray, maxDistance, out _);
        return index < 0 ? null : hits[index];
    }

    private static bool TryGetCrosshairRay(out Ray ray)
    {
        Camera camera = MainCamera.camera;
        if (!camera)
        {
            ray = default;
            return false;
        }
        // The crosshair is the screen centre. The cursor is locked there during play, but not in windowless test games.
        ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return true;
    }

    /// <summary>Fills <see cref="hits" /> and <see cref="candidates" />, returns the index of the winner (-1: none).</summary>
    private static int Collect(Ray ray, float maxDistance, out bool atHelm)
    {
        hits.Clear();
        candidates.Clear();
        Player player = Player.main;
        Vehicle? ownVehicle = player ? player.GetVehicle() : null;
        SubRoot? currentSub = player ? player.currentSub : null;
        // Mode.Piloting is the Cyclops helm only (PilotingChair); a Seamoth or Prawn suit is LockedPiloting.
        atHelm = player && player.GetMode() == Player.Mode.Piloting;

        // Solid things. The game's own aiming leaves out the Player layer too (PropulsionCannon, Builder, PlaceTool).
        int solidMask = ~LayerMask.GetMask("UI", "Ignore Raycast", "Player");
        int count = Physics.RaycastNonAlloc(ray, solidHits, maxDistance, solidMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Add(solidHits[i], ownVehicle, currentSub);
        }
        // A partner's body is a trigger on the Useable layer (RemotePlayer.SetupBody), which the solid ray leaves out.
        count = Physics.RaycastNonAlloc(ray, useableHits, maxDistance, 1 << LayerID.Useable, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Add(useableHits[i], ownVehicle, currentSub);
        }
        return PingAimRules.SelectHit(candidates, atHelm);
    }

    private static void Add(RaycastHit hit, Vehicle? ownVehicle, SubRoot? currentSub)
    {
        Collider collider = hit.collider;
        if (!collider)
        {
            return;
        }
        hits.Add(hit);
        candidates.Add(new PingAimRules.Hit(hit.distance,
                                            collider.isTrigger,
                                            collider.GetComponentInParent<RemotePlayerIdentifier>(),
                                            collider.GetComponentInParent<Player>(),
                                            ownVehicle && collider.GetComponentInParent<Vehicle>() == ownVehicle,
                                            currentSub && collider.GetComponentInParent<SubRoot>() == currentSub));
    }

#if DEBUG
    /// <summary>Dev bridge (<c>call PingAim.Describe</c>): every hit of the crosshair ray, which are skipped and which wins.</summary>
    public static Dictionary<string, object?> Describe()
    {
        return TryGetCrosshairRay(out Ray ray) ? Describe(ray, PlayerPingManager.MAX_PING_DISTANCE) : new Dictionary<string, object?> { ["error"] = "no camera" };
    }

    /// <summary>Dev bridge (<c>call PingAim.DescribeToward x,y,z</c>): the same for the ray from the camera toward a point.</summary>
    public static Dictionary<string, object?> DescribeToward(Vector3 point)
    {
        return TryGetRayToward(point, out Ray ray) ? Describe(ray, PlayerPingManager.MAX_PING_DISTANCE) : new Dictionary<string, object?> { ["error"] = "no camera" };
    }

    public static bool TryGetRayToward(Vector3 point, out Ray ray)
    {
        Camera camera = MainCamera.camera;
        if (!camera)
        {
            ray = default;
            return false;
        }
        ray = new Ray(camera.transform.position, point - camera.transform.position);
        return true;
    }

    private static Dictionary<string, object?> Describe(Ray ray, float maxDistance)
    {
        int winner = Collect(ray, maxDistance, out bool atHelm);
        List<Dictionary<string, object?>> described = [];
        for (int i = 0; i < hits.Count; i++)
        {
            Collider collider = hits[i].collider;
            PingAimRules.Hit candidate = candidates[i];
            described.Add(new Dictionary<string, object?>
            {
                ["distance"] = Math.Round(candidate.Distance, 2),
                ["object"] = collider ? collider.gameObject.name : null,
                ["root"] = collider ? Root(collider) : null,
                ["layer"] = collider ? LayerMask.LayerToName(collider.gameObject.layer) : null,
                ["trigger"] = candidate.IsTrigger,
                ["partner"] = candidate.IsPartner,
                ["ownBody"] = candidate.IsOwnBody,
                ["ownVehicle"] = candidate.IsInOwnVehicle,
                ["currentSub"] = candidate.IsInCurrentSub,
                ["skipped"] = PingAimRules.IsSkipped(candidate, atHelm),
                ["winner"] = i == winner
            });
        }
        described.Sort((a, b) => ((double)a["distance"]!).CompareTo((double)b["distance"]!));
        return new Dictionary<string, object?>
        {
            ["origin"] = new[] { Math.Round(ray.origin.x, 2), Math.Round(ray.origin.y, 2), Math.Round(ray.origin.z, 2) },
            ["direction"] = new[] { Math.Round(ray.direction.x, 3), Math.Round(ray.direction.y, 3), Math.Round(ray.direction.z, 3) },
            ["atHelm"] = atHelm,
            ["winner"] = winner < 0 ? null : Root(hits[winner].collider),
            ["winnerDistance"] = winner < 0 ? null : Math.Round(hits[winner].distance, 2),
            ["hits"] = described
        };
    }

    /// <summary>The hit's entity, as the bridge names it: the root with a NitroxId, else the top of the hierarchy.</summary>
    private static string Root(Collider collider)
    {
        NitroxEntity entity = collider.GetComponentInParent<NitroxEntity>();
        GameObject root = entity ? entity.gameObject : collider.transform.root.gameObject;
        return entity ? $"{root.name} {entity.Id}" : root.name;
    }
#endif
}
