using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC F2: what a ping says, how long it lasts, and its price (§2.2).
/// </summary>
[TestClass]
public class PingRulesTest
{
    [TestMethod]
    public void AggressiveCreatureIsDanger_EvenWhenItCouldBePickedUp()
    {
        PingRules.SelectKind(isAggressiveCreature: true, isLoot: false).Should().Be(PingKind.Danger);
        PingRules.SelectKind(isAggressiveCreature: true, isLoot: true).Should().Be(PingKind.Danger);
    }

    [TestMethod]
    public void LootIsLook_AnythingElseIsComeHere()
    {
        PingRules.SelectKind(isAggressiveCreature: false, isLoot: true).Should().Be(PingKind.Look);
        PingRules.SelectKind(isAggressiveCreature: false, isLoot: false).Should().Be(PingKind.ComeHere);
    }

    [TestMethod]
    public void CreatureIsAggressiveWhileAliveAndHuntingPlayersOrVehicles()
    {
        PingRules.IsAggressive(isAlive: true, huntsPlayer: true, attacksPilotedVehicles: false, isCrashfish: false).Should().BeTrue();
        PingRules.IsAggressive(isAlive: true, huntsPlayer: false, attacksPilotedVehicles: true, isCrashfish: false).Should().BeTrue();
        PingRules.IsAggressive(isAlive: true, huntsPlayer: false, attacksPilotedVehicles: false, isCrashfish: true).Should().BeTrue();
        PingRules.IsAggressive(isAlive: true, huntsPlayer: false, attacksPilotedVehicles: false, isCrashfish: false).Should().BeFalse("a peaceful creature");
        PingRules.IsAggressive(isAlive: false, huntsPlayer: true, attacksPilotedVehicles: true, isCrashfish: false).Should().BeFalse("a dead one bites no more");
    }

    [TestMethod]
    public void LifetimePerKind()
    {
        PingRules.Lifetime(PingKind.Danger).Should().Be(15f);
        PingRules.Lifetime(PingKind.Look).Should().Be(10f);
        PingRules.Lifetime(PingKind.ComeHere).Should().Be(30f);
    }

    [TestMethod]
    public void WithoutInterferencePingsShowAtAnyDistance()
    {
        PingRules.ShowsForReceiver(receiverInterfered: false, senderInterfered: false, distanceToSender: 240f).Should().BeTrue();
    }

    [TestMethod]
    public void InterferenceOnEitherSideLimitsPingsToNearTheSender()
    {
        PingRules.ShowsForReceiver(receiverInterfered: true, senderInterfered: false, distanceToSender: 49f).Should().BeTrue();
        PingRules.ShowsForReceiver(receiverInterfered: true, senderInterfered: false, distanceToSender: 50f).Should().BeTrue();
        PingRules.ShowsForReceiver(receiverInterfered: true, senderInterfered: false, distanceToSender: 51f).Should().BeFalse();
        PingRules.ShowsForReceiver(receiverInterfered: false, senderInterfered: true, distanceToSender: 51f).Should().BeFalse();
        PingRules.ShowsForReceiver(receiverInterfered: true, senderInterfered: true, distanceToSender: 120f).Should().BeFalse();
        PingRules.ShowsForReceiver(receiverInterfered: false, senderInterfered: true, distanceToSender: 10f).Should().BeTrue();
    }
}
