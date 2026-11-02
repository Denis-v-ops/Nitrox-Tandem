using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     Reads what the crosshair hit for a context ping (COOP_FEATURES_SPEC F2). The decision itself is
///     <see cref="PingRules" />.
/// </summary>
internal static class PingTarget
{
    /// <summary>The target of a "look" ping at a partner (MULTINAUGHTICA_SPEC M9); each game names the player itself.</summary>
    public const TechType PLAYER_TECH_TYPE = TechType.Player;

    /// <param name="hitObject">The collider's GameObject; colliders sit on child objects, so the components are looked up in its parents.</param>
    /// <param name="techType">The creature (Danger), the item or <see cref="PLAYER_TECH_TYPE" /> (Look) that was pinged, None for "come here".</param>
    public static PingKind Classify(GameObject hitObject, out TechType techType)
    {
        techType = TechType.None;
        if (!hitObject)
        {
            return PingKind.ComeHere;
        }

        // A partner's body, or what they hold: checked first, a held item is a Pickupable too.
        if (hitObject.GetComponentInParent<RemotePlayerIdentifier>())
        {
            techType = PLAYER_TECH_TYPE;
            return PingRules.SelectKind(isAggressiveCreature: false, isLoot: false, isPartner: true);
        }

        Creature creature = hitObject.GetComponentInParent<Creature>();
        bool aggressive = creature && IsAggressive(creature);
        TechType lootTechType = TechType.None;
        bool loot = !aggressive && TryGetLoot(hitObject, out lootTechType);
        PingKind kind = PingRules.SelectKind(aggressive, loot);
        techType = kind switch
        {
            PingKind.Danger => CraftData.GetTechType(creature.gameObject),
            PingKind.Look => lootTechType,
            _ => TechType.None
        };
        return kind;
    }

#if DEBUG
    /// <summary>Dev bridge (<c>call PingTarget.Describe &lt;id&gt;</c>): what a ping at this object would be, e.g. "Danger Stalker".</summary>
    public static string Describe(GameObject target)
    {
        PingKind kind = Classify(target, out TechType techType);
        return $"{kind} {techType}";
    }
#endif

    public static bool IsAggressive(Creature creature)
    {
        bool isAlive = !creature.liveMixin || creature.liveMixin.IsAlive();
        // What the player is to creatures. Read from the player rather than assumed (the game's CreatureData calls the player a "Shark").
        EcoTargetType playerTargetType = Player.main && Player.main.TryGetComponent(out EcoTarget playerEcoTarget) ? playerEcoTarget.GetTargetType() : EcoTargetType.Shark;
        bool huntsPlayer = false;
        foreach (AggressiveWhenSeeTarget aggression in creature.GetComponentsInChildren<AggressiveWhenSeeTarget>(true))
        {
            if (aggression is AggressiveWhenSeePlayer || aggression.targetType == playerTargetType)
            {
                huntsPlayer = true;
                break;
            }
        }
        bool attacksPilotedVehicles = creature.GetComponentInChildren<AggressiveToPilotingVehicle>(true);
        return PingRules.IsAggressive(isAlive, huntsPlayer, attacksPilotedVehicles, creature is Crash);
    }

    /// <summary>
    ///     Something to pick up (its own tech type), a breakable outcrop (the outcrop's) or a drillable deposit (its main
    ///     resource).
    /// </summary>
    public static bool TryGetLoot(GameObject hitObject, out TechType techType)
    {
        Pickupable pickupable = hitObject.GetComponentInParent<Pickupable>();
        if (pickupable)
        {
            techType = pickupable.GetTechType();
            return true;
        }
        BreakableResource breakable = hitObject.GetComponentInParent<BreakableResource>();
        if (breakable)
        {
            techType = CraftData.GetTechType(breakable.gameObject);
            return true;
        }
        Drillable drillable = hitObject.GetComponentInParent<Drillable>();
        if (drillable)
        {
            techType = drillable.GetDominantResourceType();
            return true;
        }
        techType = TechType.None;
        return false;
    }
}
