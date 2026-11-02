using static NitroxClient.GameLogic.BleederClaimRules;

namespace NitroxClient.GameLogic;

/// <summary>
///     MULTINAUGHTICA_SPEC M4 (FIX_SPEC S15d): the player whose game accepted a Bleeder's contact takes its exclusive lock,
///     so the attack runs vanilla there (<see cref="BleederClaims" />). Ported from MultiNaughtica.
/// </summary>
[TestClass]
public class BleederClaimRulesTest
{
    [TestMethod]
    public void ARequestIsSentForAFreshContact()
    {
        ShouldRequestClaim(holdsExclusiveLock: false, secondsSinceRequest: null).Should().BeTrue();
    }

    [TestMethod]
    public void NothingIsRequestedWhenWeHoldTheBleederExclusively()
    {
        ShouldRequestClaim(holdsExclusiveLock: true, secondsSinceRequest: null).Should().BeFalse();
        ShouldRequestClaim(holdsExclusiveLock: true, secondsSinceRequest: 30f).Should().BeFalse();
    }

    [TestMethod]
    public void AnUnansweredRequestIsNotRepeatedBeforeTheTimeout()
    {
        ShouldRequestClaim(holdsExclusiveLock: false, secondsSinceRequest: 0.5f).Should().BeFalse();
        ShouldRequestClaim(holdsExclusiveLock: false, secondsSinceRequest: CLAIM_ANSWER_TIMEOUT - 0.1f).Should().BeFalse();
    }

    [TestMethod]
    public void ALostAnswerIsAskedForAgainAfterTheTimeout()
    {
        ShouldRequestClaim(holdsExclusiveLock: false, secondsSinceRequest: CLAIM_ANSWER_TIMEOUT).Should().BeTrue();
    }

    [TestMethod]
    public void ARefusedClaimUndoesTheContact()
    {
        JudgeClaimResponse(lockAcquired: false, stillHasTarget: true).Should().Be(ClaimOutcome.Detach);
        // Even if the Bleeder let go meanwhile: detaching a Bleeder without a target only starts its 4 s pause
        JudgeClaimResponse(lockAcquired: false, stillHasTarget: false).Should().Be(ClaimOutcome.Detach);
    }

    [TestMethod]
    public void AGrantedClaimKeepsTheLockWhileTheBleederIsOnItsTarget()
    {
        JudgeClaimResponse(lockAcquired: true, stillHasTarget: true).Should().Be(ClaimOutcome.KeepLock);
    }

    [TestMethod]
    public void AGrantedClaimOnABleederThatLetGoMeanwhileGivesTheLockBack()
    {
        JudgeClaimResponse(lockAcquired: true, stillHasTarget: false).Should().Be(ClaimOutcome.ReleaseLock);
    }

    [TestMethod]
    public void TheLockGoesBackAfterTheBleederLetGo()
    {
        ShouldReleaseLockAfterDetach(holdsExclusiveLock: true, componentActive: true, bleederAlive: true).Should().BeTrue();
    }

    [TestMethod]
    public void NoLockIsGivenBackWhenWeDoNotHoldItExclusively()
    {
        ShouldReleaseLockAfterDetach(holdsExclusiveLock: false, componentActive: true, bleederAlive: true).Should().BeFalse();
    }

    [TestMethod]
    public void NoLockIsGivenBackForABleederThatIsLeavingTheWorld()
    {
        // OnDisable calls SetDetached: the entity is going away and the server revokes the lock itself
        ShouldReleaseLockAfterDetach(holdsExclusiveLock: true, componentActive: false, bleederAlive: true).Should().BeFalse();
        // A killed Bleeder: its entity is destroyed
        ShouldReleaseLockAfterDetach(holdsExclusiveLock: true, componentActive: true, bleederAlive: false).Should().BeFalse();
    }

    [TestMethod]
    public void AClaimWhoseAttackNeverStartedIsAbandoned()
    {
        ShouldAbandonClaim(holdsExclusiveLock: true, hasTarget: true, attackActive: false).Should().BeTrue();
    }

    [TestMethod]
    public void AClaimIsKeptWhileTheAttackRuns_OrWhenThereIsNothingToUndo()
    {
        ShouldAbandonClaim(holdsExclusiveLock: true, hasTarget: true, attackActive: true).Should().BeFalse();
        // The Bleeder let go already (its exit gives the lock back)
        ShouldAbandonClaim(holdsExclusiveLock: true, hasTarget: false, attackActive: false).Should().BeFalse();
        // The lock went to somebody else meanwhile
        ShouldAbandonClaim(holdsExclusiveLock: false, hasTarget: true, attackActive: false).Should().BeFalse();
    }

    [TestMethod]
    public void AnAttackInProgressIsEndedWhenTheLockIsLost()
    {
        NeedsDetachAfterLockLoss(attackActive: true, hasTarget: true).Should().BeTrue();
        // Contact accepted and the attack not started yet, or ended with the target kept
        NeedsDetachAfterLockLoss(attackActive: false, hasTarget: true).Should().BeTrue();
        NeedsDetachAfterLockLoss(attackActive: true, hasTarget: false).Should().BeTrue();
    }

    [TestMethod]
    public void AnIdleBleederNeedsNothingWhenTheLockIsLost()
    {
        NeedsDetachAfterLockLoss(attackActive: false, hasTarget: false).Should().BeFalse();
    }
}
