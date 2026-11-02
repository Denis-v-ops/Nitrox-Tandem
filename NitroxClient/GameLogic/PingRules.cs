using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic;

/// <summary>
///     The rules of the context pings (COOP_FEATURES_SPEC F2) as pure functions. <see cref="PingTarget" /> reads the facts
///     from the game.
/// </summary>
internal static class PingRules
{
    /// <summary>When the sender or the receiver is in interference, a ping only shows within this distance of the sender.</summary>
    public const float INTERFERENCE_RANGE = 50f;

    /// <summary>A "come here" ping is done when the partner is this close to it.</summary>
    public const float ARRIVAL_DISTANCE = 4f;

    /// <summary>
    ///     A live aggressive creature is a danger; something to pick up, break or drill is worth a look; anything else
    ///     (ground, a wall, a peaceful creature) means "come here".
    /// </summary>
    public static PingKind SelectKind(bool isAggressiveCreature, bool isLoot)
    {
        if (isAggressiveCreature)
        {
            return PingKind.Danger;
        }
        return isLoot ? PingKind.Look : PingKind.ComeHere;
    }

    /// <summary>
    ///     A creature is aggressive while it lives and either hunts what the player is to it (an
    ///     <c>AggressiveWhenSeeTarget</c> aimed at the player's eco target type, or <c>AggressiveWhenSeePlayer</c>), attacks
    ///     piloted vehicles, or is a crashfish (which explodes at the player without either).
    /// </summary>
    public static bool IsAggressive(bool isAlive, bool huntsPlayer, bool attacksPilotedVehicles, bool isCrashfish)
    {
        return isAlive && (huntsPlayer || attacksPilotedVehicles || isCrashfish);
    }

    /// <summary>Seconds a ping lasts. A "come here" ping also ends when the partner arrives (<see cref="ARRIVAL_DISTANCE" />).</summary>
    public static float Lifetime(PingKind kind)
    {
        return kind switch
        {
            PingKind.Danger => 15f,
            PingKind.Look => 10f,
            _ => 30f
        };
    }

    /// <summary>
    ///     The price of a ping (COOP_FEATURES_SPEC §2.2): pings travel like the partner signal. When the sender or the
    ///     receiver is in interference, the receiver only shows a ping while within <see cref="INTERFERENCE_RANGE" /> of
    ///     the sender.
    /// </summary>
    public static bool ShowsForReceiver(bool receiverInterfered, bool senderInterfered, float distanceToSender)
    {
        return !(receiverInterfered || senderInterfered) || distanceToSender <= INTERFERENCE_RANGE;
    }
}
