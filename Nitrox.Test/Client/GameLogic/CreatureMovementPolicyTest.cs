using System;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using static NitroxClient.GameLogic.Simulation.CreatureMovementPolicy;

namespace NitroxClient.GameLogic;

/// <summary>
///     FIX_SPEC S16b: how often the simulating game sends a creature's pose, and that a full packet of them fits the
///     unreliable packet limit.
/// </summary>
[TestClass]
public class CreatureMovementPolicyTest
{
    private const int NEAR = 0;
    private const int MEDIUM = 1;
    private const int FAR = 2;
    private const int VERY_FAR = 3;

    [TestMethod]
    public void LeviathanNearAPartner_IsSentTenTimesASecond()
    {
        SendPeriod(VERY_FAR, 300f, moving: true).Should().Be(LEVIATHAN_PERIOD);
        SendPeriod(FAR, 100f, moving: true).Should().Be(LEVIATHAN_PERIOD);
        LEVIATHAN_PERIOD.Should().Be(0.1f);
    }

    [TestMethod]
    public void SmallCreature_FasterCloseToAPartner()
    {
        SendPeriod(NEAR, 10f, moving: true).Should().Be(CLOSE_PERIOD);
        SendPeriod(NEAR, CLOSE_DISTANCE + 1, moving: true).Should().Be(NEAR_PERIOD);
        SendPeriod(MEDIUM, 150f, moving: true).Should().Be(NEAR_PERIOD);
    }

    [TestMethod]
    public void NobodyElseHasItLoaded_OnlyKeepsTheServerRecordFresh()
    {
        SendPeriod(NEAR, LoadRange(NEAR) + 1, moving: true).Should().Be(FAR_PERIOD);
        SendPeriod(VERY_FAR, LoadRange(VERY_FAR) + 1, moving: true).Should().Be(FAR_PERIOD);
        SendPeriod(VERY_FAR, float.PositiveInfinity, moving: true).Should().Be(FAR_PERIOD);
        // Levels above VeryFar (batch, global) load as far as VeryFar
        LoadRange(10).Should().Be(LoadRange(VERY_FAR));
    }

    [TestMethod]
    public void LoadRangesCoverTheGamesLoadedBox()
    {
        // ±64 m for level 0, doubling per level; ±512 m for VeryFar was read in game (S16)
        for (int level = NEAR; level <= VERY_FAR; level++)
        {
            LoadRange(level).Should().BeGreaterThan(64 << level);
        }
    }

    [TestMethod]
    public void RestingCreature_SentOnlyWhenItMoved()
    {
        IsMoving(speed: 0.05f, lastSentSpeed: 0.05f).Should().BeFalse();
        SendPeriod(VERY_FAR, 50f, moving: false).Should().Be(FAR_PERIOD);
        ShouldSend(sinceLastSend: 10f, FAR_PERIOD, moving: false, movedSinceLastSend: 0.01f).Should().BeFalse();
        ShouldSend(sinceLastSend: 10f, FAR_PERIOD, moving: false, movedSinceLastSend: 0.5f).Should().BeTrue();
    }

    [TestMethod]
    public void CreatureThatJustStopped_IsSentOnceMore()
    {
        // The partner's game still carries it forward with the last velocity it heard of
        IsMoving(speed: 0f, lastSentSpeed: 12f).Should().BeTrue();
        IsMoving(speed: 12f, lastSentSpeed: 0f).Should().BeTrue();
    }

    [TestMethod]
    public void NotBeforeItsPeriod_AndAlwaysTheFirstTime()
    {
        ShouldSend(sinceLastSend: 0.05f, LEVIATHAN_PERIOD, moving: true, movedSinceLastSend: 1f).Should().BeFalse();
        ShouldSend(sinceLastSend: 0.1f, LEVIATHAN_PERIOD, moving: true, movedSinceLastSend: 1f).Should().BeTrue();
        ShouldSend(float.PositiveInfinity, FAR_PERIOD, moving: false, movedSinceLastSend: float.PositiveInfinity).Should().BeTrue();
    }

    /// <summary>
    ///     LiteNetLib refuses an unreliable packet larger than the path MTU, which starts at 508 bytes (576 minus the largest
    ///     UDP/IP header) and only grows with MTU discovery. Leaves room for LiteNetLib's and Nitrox's own headers.
    /// </summary>
    [TestMethod]
    public void FullPacketFitsTheSmallestMtu()
    {
        Packet.InitSerializer();
        List<CreatureMovement> data = Enumerable.Range(0, EntityPositionBroadcaster.MAX_CREATURES_PER_PACKET)
                                                .Select(i => new CreatureMovement(new NitroxId(Guid.NewGuid()), new NitroxVector3(1000.5f, -300.25f, i), new NitroxQuaternion(0.1f, 0.2f, 0.3f, 0.9f),
                                                                                  new NitroxVector3(12, -1, 3), new NitroxVector3(0.1f, 0.2f, 0.3f)))
                                                .ToList();
        byte[] serialized = new CreatureMovements(data, 123456.789).Serialize();
        Console.WriteLine($"{data.Count} creatures: {serialized.Length} bytes");
        serialized.Length.Should().BeLessThan(450);
    }
}
