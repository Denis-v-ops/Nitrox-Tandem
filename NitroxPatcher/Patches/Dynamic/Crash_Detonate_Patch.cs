using System;
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
/// </summary>
public sealed partial class Crash_Detonate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Crash t) => t.Detonate());

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
            // Same falloff as DamageSystem.RadiusDamage (game 83031)
            float distance = (center - remotePlayer.Body.transform.position).magnitude;
            float damage = __instance.maxDamage * Math.Max(0f, (__instance.detonateRadius - distance) / __instance.detonateRadius);
            if (damage > 0f)
            {
                Resolve<IPacketSender>().Send(new CreatureAttackedPlayer(crashId, remotePlayer.SessionId, damage, (ushort)DamageType.Explosive));
            }
        }
    }
}
