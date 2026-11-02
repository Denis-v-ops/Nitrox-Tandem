using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LiveMixin_AddHealth_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((LiveMixin t) => t.AddHealth(default));

    /// <summary>
    ///     We can broadcast packet update when we aren't processing remote health change
    /// </summary>
    private static bool CanBroadcast => !Resolve<LiveMixinManager>().IsRemoteHealthChanging;

    public static bool Prefix(out float __state, LiveMixin __instance, float healthBack)
    {
        // Persist the previous health value
        __state = __instance.health;

        if (HoldsLastRadiationLeak(__instance, healthBack))
        {
            return false;
        }

        if (!Resolve<LiveMixinManager>().IsWhitelistedUpdateType(__instance))
        {
            return true; // everyone should process this locally
        }

        return Resolve<LiveMixinManager>().ShouldApplyNextHealthUpdate(__instance);
    }

    public static void Postfix(float __state, LiveMixin __instance, bool __runOriginal)
    {
        if (!__runOriginal || __state == __instance.health)
        {
            return;
        }

        // This foreach will detect the presence of some components which need to be handled specifically
        foreach (MonoBehaviour monoBehaviour in __instance.GetComponents<MonoBehaviour>())
        {
            switch (monoBehaviour)
            {
                case RadiationLeak radiationLeak:
                    HandleRadiationLeakRepair(radiationLeak);
                    return;
                case CyclopsDamagePoint cyclopsDamagePoint:
                    HandleCyclopsDamagePointRepair(__instance, cyclopsDamagePoint);
                    return;
                case BaseCell baseCell:
                    HandleBaseLeakRepair(baseCell, __instance);
                    return;
            }
        }
        // If the foreach ends, it'll mean that none of those components will have been detected so we just execute the generic case
        HandleGenericEntity(__instance);
    }

    /// <summary>
    ///     COOP_FEATURES_SPEC G4: the heal that would seal the Aurora's last radiation leak waits until everyone on the
    ///     roster is in the drive core room. Sealing it makes <c>RadiationLeak.OnHealDamage</c> call
    ///     <c>LeakingRadiation.NotifyLeaksFixed</c>, which triggers <c>leaksFixedGoal</c>; while held, the welder's
    ///     <c>AddHealth</c> returns 0, so it uses no energy. When the server starts the gate the heal runs again and its
    ///     metadata reaches the partner, whose game seals the leak through <see cref="LiveMixinManager.SyncRemoteHealth" />.
    ///     That remote heal is never held: it applies what the server already accepted.
    /// </summary>
    private static bool HoldsLastRadiationLeak(LiveMixin liveMixin, float healthBack)
    {
        StoryGates storyGates = Resolve<StoryGates>();
        if (storyGates.IsPassing(StoryGateKind.DRIVE_CORE) || Resolve<LiveMixinManager>().IsRemoteHealthChanging ||
            !liveMixin.TryGetComponent(out RadiationLeak leak) || !IsLastLeak(leak))
        {
            return false;
        }
        // The same checks as LiveMixin.AddHealth: only a heal that reaches full health seals the leak.
        if (!(liveMixin.IsAlive() || liveMixin.canResurrect) || !(liveMixin.health + healthBack >= liveMixin.maxHealth))
        {
            return false;
        }

        storyGates.Hold(StoryGateKind.DRIVE_CORE, () =>
        {
            if (liveMixin && leak.IsLeaking())
            {
                liveMixin.AddHealth(liveMixin.maxHealth);
            }
        });
        return true;
    }

    /// <summary>
    ///     The same count as <c>LeakingRadiation.GetNumLeaks</c>, which decides whether <c>NotifyLeaksFixed</c> completes
    ///     the goal: this leak is the only one of the registered leaks still leaking.
    /// </summary>
    private static bool IsLastLeak(RadiationLeak leak)
    {
        LeakingRadiation leakingRadiation = LeakingRadiation.main;
        if (!leakingRadiation || !leak.liveMixin || !leak.IsLeaking())
        {
            return false;
        }
        foreach (RadiationLeak other in leakingRadiation.leaks)
        {
            if (other && other != leak && other.IsLeaking())
            {
                return false;
            }
        }
        return true;
    }

    private static void HandleRadiationLeakRepair(RadiationLeak radiationLeak)
    {
        if (!CanBroadcast || !radiationLeak.TryGetNitroxId(out NitroxId leakId))
        {
            return;
        }

        Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(radiationLeak);
        if (metadata.HasValue)
        {
            Resolve<Entities>().BroadcastMetadataUpdate(leakId, metadata.Value);
        }
    }

    private static void HandleCyclopsDamagePointRepair(LiveMixin liveMixin, CyclopsDamagePoint cyclopsDamagePoint)
    {
        if (!CanBroadcast || !cyclopsDamagePoint.TryGetNitroxId(out NitroxId leakId))
        {
            return;
        }

        if (liveMixin.IsFullHealth())
        {
            // Entity was already destroyed
            return;
        }

        Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(cyclopsDamagePoint);
        if (metadata.HasValue)
        {
            Resolve<Entities>().BroadcastMetadataUpdate(leakId, metadata.Value);
        }
    }

    private static void HandleBaseLeakRepair(BaseCell baseCell, LiveMixin liveMixin)
    {
        if (liveMixin.IsFullHealth())
        {
            if (liveMixin.TryGetComponentInParent(out BaseLeakManager baseLeakManager, true))
            {
                LeakRepaired leakRepaired = baseLeakManager.RemoveLeakByAbsoluteCell(baseCell.cell);
                if (CanBroadcast && leakRepaired != null)
                {
                    Resolve<IPacketSender>().Send(leakRepaired);
                }
            }
        }
        else if (liveMixin.TryGetComponentInParent(out BaseHullStrength baseHullStrength, true))
        {
            BaseHullStrength_CrushDamageUpdate_Patch.BroadcastChange(baseHullStrength, liveMixin);
        }
    }

    private static void HandleGenericEntity(LiveMixin victim)
    {
        // Let others know if we have a lock on this entity
        if (!CanBroadcast || !victim.TryGetIdOrWarn(out NitroxId id) || !Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            return;
        }

        Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(victim.gameObject);
        if (metadata.HasValue)
        {
            Resolve<Entities>().BroadcastMetadataUpdate(id, metadata.Value);
        }
    }
}
