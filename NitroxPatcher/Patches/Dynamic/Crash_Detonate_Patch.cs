using System;
using System.Collections.Generic;
using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// FIX_SPEC S14: a crash fish explodes only in the game that simulates it (its state machine runs in <see cref="ProtectCrashHome" />).
/// <see cref="DamageSystem.RadiusDamage" /> there can't hurt the other players (their bodies are invincible and it passes no
/// dealer), so each other player in range gets the same damage, which their own game applies.
/// M4: the same for vehicles whose lock another player holds (the partner's Seamoth, Prawn suit or Cyclops): only the lock
/// holder's game changes a vehicle's health, and COOP D4's relay needs a dealer, which the blast doesn't pass.
/// </summary>
public sealed partial class Crash_Detonate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Crash t) => t.Detonate());

    private static readonly HashSet<LiveMixin> blastedVehicles = [];

    public static void Prefix(Crash __instance)
    {
        if (!__instance.TryGetNitroxId(out NitroxId crashId) || !Resolve<SimulationOwnership>().HasAnyLockType(crashId))
        {
            return;
        }

        Vector3 center = __instance.transform.position;
        foreach (RemotePlayer remotePlayer in Resolve<PlayerManager>().GetAll())
        {
            if (!remotePlayer.Body || !remotePlayer.LiveMixin || !remotePlayer.LiveMixin.IsAlive() || !remotePlayer.CanBeAttacked())
            {
                continue;
            }
            float damage = BlastDamage(__instance, center, remotePlayer.Body.transform.position);
            if (damage > 0f)
            {
                Resolve<IPacketSender>().Send(new CreatureAttackedPlayer(crashId, remotePlayer.SessionId, damage, (ushort)DamageType.Explosive));
            }
        }

        RelayBlastOnOthersVehicles(__instance, crashId, center);
    }

    /// <summary>
    ///     The vehicles <see cref="DamageSystem.RadiusDamage" /> will reach (any collider in the radius, the LiveMixin above
    ///     it, game 83031) whose lock this game doesn't hold. The server sends each blast to the vehicle's lock holder and
    ///     drops it when nobody holds the vehicle (<c>CreatureAttackedVehicleProcessor</c>).
    /// </summary>
    private static void RelayBlastOnOthersVehicles(Crash crash, NitroxId crashId, Vector3 center)
    {
        LiveMixinManager liveMixinManager = Resolve<LiveMixinManager>();
        SimulationOwnership simulationOwnership = Resolve<SimulationOwnership>();
        int count = UWE.Utils.OverlapSphereIntoSharedBuffer(center, crash.detonateRadius);
        try
        {
            for (int i = 0; i < count; i++)
            {
                Collider collider = UWE.Utils.sharedColliderBuffer[i];
                if (collider.gameObject == crash.gameObject)
                {
                    continue;
                }
                LiveMixin vehicle = global::Utils.FindAncestorWithComponent<LiveMixin>(collider.gameObject);
                if (!vehicle || !liveMixinManager.IsWhitelistedUpdateType(vehicle) || !blastedVehicles.Add(vehicle) ||
                    !vehicle.TryGetNitroxId(out NitroxId vehicleId) || simulationOwnership.HasAnyLockType(vehicleId))
                {
                    continue;
                }
                float damage = BlastDamage(crash, center, vehicle.transform.position);
                if (damage > 0f)
                {
                    Resolve<IPacketSender>().Send(new CreatureAttackedVehicle(crashId, vehicleId, damage, (ushort)DamageType.Explosive));
                }
            }
        }
        finally
        {
            blastedVehicles.Clear();
        }
    }

    /// <summary>
    ///     Same falloff as <see cref="DamageSystem.RadiusDamage" /> (game 83031), from the target's root position.
    /// </summary>
    private static float BlastDamage(Crash crash, Vector3 center, Vector3 target)
    {
        float distance = (center - target).magnitude;
        return crash.maxDamage * Math.Max(0f, (crash.detonateRadius - distance) / crash.detonateRadius);
    }
}
