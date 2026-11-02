using static NitroxClient.GameLogic.CreatureDamageRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M5: only the game that simulates a creature changes its health (<see cref="CreatureHealthManager" />).
///     These are its decisions for one hit, in the game where the hit happens.
/// </summary>
[TestClass]
public class CreatureDamageRulesTest
{
    /// <summary>
    ///     The regression of upstream #2903 as it was: a predator and a prey simulated by different players. The bite happens
    ///     only in the predator's simulating game (its copy elsewhere isn't aggressive, FIX_SPEC S14) and counted as
    ///     environment damage there, which that game only shows: the prey never took damage anywhere.
    /// </summary>
    [TestMethod]
    public void ABiteByACreatureThisGameSimulates_OnACreatureAnotherGameSimulates_IsSentThere()
    {
        HitSource source = CreatureDealerSource(attacksFromSimulator: true, dealerSimulatedHere: true, dealerSimulatedElsewhere: false);

        source.Should().Be(HitSource.Local);
        DecideDamage(source, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.ForwardAndShow);
    }

    [TestMethod]
    public void ABiteByACreatureThisGameSimulates_OnACreatureItAlsoSimulates_Applies()
    {
        HitSource source = CreatureDealerSource(attacksFromSimulator: true, dealerSimulatedHere: true, dealerSimulatedElsewhere: false);

        DecideDamage(source, holdsCreature: true, streamedByAnotherPlayer: false, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Apply);
    }

    /// <summary>
    ///     A copy of another game's creature that bites here (e.g. it got aggressive from a hit shown here) must not count:
    ///     its simulating game decides the creature's bites and sends them.
    /// </summary>
    [TestMethod]
    public void ABiteByAnotherGamesCreature_OnACreatureThisGameSimulates_IsDropped()
    {
        HitSource source = CreatureDealerSource(attacksFromSimulator: true, dealerSimulatedHere: false, dealerSimulatedElsewhere: true);

        source.Should().Be(HitSource.RemoteCopy);
        DecideDamage(source, holdsCreature: true, streamedByAnotherPlayer: false, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Drop);
        DecideDamage(source, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Show);
    }

    /// <summary>
    ///     Every copy of the four aggression-synced leviathans bites (FIX_SPEC S14): each game's copy of the prey takes the
    ///     bite, and only the prey's simulating game counts it.
    /// </summary>
    [TestMethod]
    public void ALeviathansBite_CountsWhereThePreyIsSimulated()
    {
        foreach (bool dealerSimulatedHere in new[] { true, false })
        {
            HitSource source = CreatureDealerSource(attacksFromSimulator: false, dealerSimulatedHere, dealerSimulatedElsewhere: !dealerSimulatedHere);

            source.Should().Be(HitSource.World);
            DecideDamage(source, holdsCreature: true, streamedByAnotherPlayer: false, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Apply);
            DecideDamage(source, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Show);
        }
    }

    [TestMethod]
    public void ABiteByACreatureNobodySimulates_IsEnvironmentDamage()
    {
        CreatureDealerSource(attacksFromSimulator: true, dealerSimulatedHere: false, dealerSimulatedElsewhere: false).Should().Be(HitSource.World);
    }

    [TestMethod]
    public void EnvironmentDamage_CountsWhereTheCreatureIsSimulated()
    {
        DecideDamage(HitSource.World, holdsCreature: true, streamedByAnotherPlayer: false, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Apply);
        DecideDamage(HitSource.World, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Show);
    }

    [TestMethod]
    public void EnvironmentDamage_OnACreatureNobodyStreams_StaysLocal()
    {
        DecideDamage(HitSource.World, holdsCreature: false, streamedByAnotherPlayer: false, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.Apply);
    }

    /// <summary>
    ///     The local player's hit (knife, Prawn suit arms, a crash fish blast this game simulates) on a creature it doesn't
    ///     simulate: sent, and only shown here. Also when nobody streams the creature: the server sends it back if nobody
    ///     simulates it.
    /// </summary>
    [TestMethod]
    public void TheLocalPlayersHit_OnACreatureThisGameDoesNotSimulate_IsSent()
    {
        DecideDamage(HitSource.Local, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.ForwardAndShow);
        DecideDamage(HitSource.Local, holdsCreature: false, streamedByAnotherPlayer: false, canBeHurt: true, positiveDamage: true).Should().Be(DamageOutcome.ForwardAndShow);
    }

    [TestMethod]
    public void AHitOnADeadOrInvincibleCreature_IsDropped()
    {
        DecideDamage(HitSource.Local, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: false, positiveDamage: true).Should().Be(DamageOutcome.Drop);
    }

    [TestMethod]
    public void ANegativeHit_IsNotSent()
    {
        DecideDamage(HitSource.Local, holdsCreature: false, streamedByAnotherPlayer: true, canBeHurt: true, positiveDamage: false).Should().Be(DamageOutcome.Show);
    }

    [TestMethod]
    public void ThePreviousSimulatorsReport_IsTakenRightAfterTheHandOver()
    {
        AcceptsReportAfterHandOver(secondsSinceLockTaken: 0.3f, secondsSinceLocalChange: null).Should().BeTrue();
        AcceptsReportAfterHandOver(secondsSinceLockTaken: HAND_OVER_GRACE_TIME, secondsSinceLocalChange: null).Should().BeTrue();
    }

    [TestMethod]
    public void ThePreviousSimulatorsReport_IsRefusedAfterTheGraceTime()
    {
        AcceptsReportAfterHandOver(secondsSinceLockTaken: HAND_OVER_GRACE_TIME + 0.1f, secondsSinceLocalChange: null).Should().BeFalse();
    }

    [TestMethod]
    public void ThePreviousSimulatorsReport_IsRefusedOnceTheHealthChangedHereSinceTheHandOver()
    {
        AcceptsReportAfterHandOver(secondsSinceLockTaken: 0.5f, secondsSinceLocalChange: 0.2f).Should().BeFalse();
        // A change from before the take-over (this game simulated the creature earlier) doesn't count
        AcceptsReportAfterHandOver(secondsSinceLockTaken: 0.5f, secondsSinceLocalChange: 30f).Should().BeTrue();
    }
}
