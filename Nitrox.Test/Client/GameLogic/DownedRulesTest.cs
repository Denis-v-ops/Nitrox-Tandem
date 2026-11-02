using NitroxClient.GameLogic;

namespace Nitrox.Test.Client.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC T1: which kills of the local player put them down and which go through (L1).
/// </summary>
[TestClass]
public class DownedRulesTest
{
    private static bool GoesDown(bool featureOn = true, bool alreadyDowned = false, float oxygen = 30, bool subDestroyed = false, DamageType type = DamageType.Normal) =>
        DownedRules.GoesDown(new DownedKillContext(featureOn, alreadyDowned, oxygen, subDestroyed, type));

    [TestMethod]
    public void LethalDamage_PutsThePlayerDown()
    {
        foreach (DamageType type in new[] { DamageType.Normal, DamageType.Puncture, DamageType.Electrical, DamageType.Poison, DamageType.Cold, DamageType.Explosive, DamageType.Fire, DamageType.Starve })
        {
            GoesDown(type: type).Should().BeTrue($"{type} damage gives a rescue window");
        }
    }

    [TestMethod]
    public void TheseKillsGoThrough()
    {
        GoesDown(featureOn: false).Should().BeFalse("RescueWindowSeconds 0 turns the feature off");
        GoesDown(alreadyDowned: true).Should().BeFalse("drowning and the timer kill a downed player");
        GoesDown(oxygen: 0).Should().BeFalse("out of air: drowning kills");
        GoesDown(oxygen: 0.4f).Should().BeFalse("less than a second of air counts as out of air");
        GoesDown(subDestroyed: true).Should().BeFalse("the Cyclops explosion kills everyone aboard");
        GoesDown(type: DamageType.Heat).Should().BeFalse("lava and heat (TemperatureDamage deals Heat) kill");
    }

    [TestMethod]
    public void ReviveHoldTimes()
    {
        DownedRules.ReviveSeconds(kitInHand: false).Should().Be(6);
        DownedRules.ReviveSeconds(kitInHand: true).Should().Be(2);
    }
}
