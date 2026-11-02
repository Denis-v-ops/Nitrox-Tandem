using System.Reflection;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.PlayerPainSound;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Sends the pain sound the local player's game plays when they get hurt, so the partner hears it on their body
///     (COOP_FEATURES_SPEC D1). At most two a second.
/// </summary>
public sealed partial class PlayerDamageSounds_OnTakeDamage_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((PlayerDamageSounds t) => t.OnTakeDamage(default));

    private const float MIN_INTERVAL = 0.5f;
    private static float lastSentTime = float.NegativeInfinity;

    public static void Postfix(PlayerDamageSounds __instance, DamageInfo damageInfo)
    {
        Player player = Player.main;
        if (!player || !__instance.transform.IsChildOf(player.transform) || Time.time < lastSentTime + MIN_INTERVAL)
        {
            return;
        }
        // The game's own inputs at the same moment, so this is the emitter it just picked. It counts tanks in the inventory,
        // not the equipped one, so a player with a tank only on the back gets "no tank": the partner hears the same.
        if (!TryGetKind(damageInfo.damage, damageInfo.type, player.isUnderwater.value, Inventory.Get().GetPickupCount(TechType.Tank), out PainKind kind))
        {
            return;
        }
        SessionId? sessionId = Resolve<LocalPlayer>().SessionId;
        if (!sessionId.HasValue)
        {
            return;
        }
        lastSentTime = Time.time;
        Resolve<IPacketSender>().Send(new PlayerPainSound(sessionId.Value, kind));
    }

    /// <summary>
    ///     The branches of <see cref="PlayerDamageSounds.OnTakeDamage" />: which emitter it plays, if any.
    /// </summary>
    internal static bool TryGetKind(float damage, DamageType type, bool isUnderwater, int tankCount, out PainKind kind)
    {
        kind = default;
        if (!(damage > 0f) || type == DamageType.Radiation)
        {
            return false;
        }
        if (type == DamageType.Smoke)
        {
            kind = PainKind.SMOKE;
        }
        else if (!isUnderwater)
        {
            kind = PainKind.WITHOUT_MASK;
        }
        else if (tankCount != 0)
        {
            kind = PainKind.WITH_TANK;
        }
        else if (damage > 8f)
        {
            kind = PainKind.WITHOUT_TANK;
        }
        else
        {
            kind = PainKind.WITHOUT_TANK_LIGHT;
        }
        return true;
    }
}
