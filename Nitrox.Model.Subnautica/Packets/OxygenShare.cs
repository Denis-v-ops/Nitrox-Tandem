using System;
using Nitrox.Model.Core;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Buddy breathing (COOP_FEATURES_SPEC T2). The donor's game sends the oxygen it took from its own tank; the server
///     checks the two players can reach each other, clamps the amount, scales it by <c>BuddyBreathingEfficiency</c> and
///     sends the result to the target only, which adds it to its oxygen.
/// </summary>
[Serializable]
public sealed class OxygenShare : Packet
{
    /// <summary>
    ///     The most oxygen (seconds of air at the surface) one packet may carry from the donor. The game sends one packet
    ///     every 0.5 s.
    /// </summary>
    public const float MAX_AMOUNT = 1f;

    /// <summary>
    ///     The player who gave the air. The server sets it to the sender.
    /// </summary>
    public SessionId DonorId { get; }

    public SessionId TargetId { get; }

    /// <summary>
    ///     From the donor: what it paid. From the server: what the target gets.
    /// </summary>
    public float Amount { get; }

    public OxygenShare(SessionId donorId, SessionId targetId, float amount)
    {
        DonorId = donorId;
        TargetId = targetId;
        Amount = amount;
    }
}
