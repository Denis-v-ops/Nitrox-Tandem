using NitroxPatcher.Patches.Dynamic;
using static Nitrox.Model.Subnautica.Packets.PlayerPainSound;

namespace Nitrox.Test.Patcher.Patches.Dynamic;

/// <summary>
///     COOP_FEATURES_SPEC D1: the pain kind sent is the emitter <c>PlayerDamageSounds.OnTakeDamage</c> picks (L1).
/// </summary>
[TestClass]
public class PlayerDamageSounds_OnTakeDamage_PatchTest
{
    [TestMethod]
    public void Kind_FollowsTheGamesBranches()
    {
        Kind(10, DamageType.Normal, underwater: true, tanks: 0).Should().Be(PainKind.WITHOUT_TANK);
        Kind(4, DamageType.Normal, underwater: true, tanks: 0).Should().Be(PainKind.WITHOUT_TANK_LIGHT);
        Kind(8, DamageType.Normal, underwater: true, tanks: 0).Should().Be(PainKind.WITHOUT_TANK_LIGHT, "only more than 8 is the heavy one");
        Kind(4, DamageType.Normal, underwater: true, tanks: 1).Should().Be(PainKind.WITH_TANK);
        Kind(30, DamageType.Normal, underwater: false, tanks: 1).Should().Be(PainKind.WITHOUT_MASK);
        Kind(1, DamageType.Smoke, underwater: false, tanks: 1).Should().Be(PainKind.SMOKE);
    }

    [TestMethod]
    public void NoSound_ForRadiationOrNoDamage()
    {
        PlayerDamageSounds_OnTakeDamage_Patch.TryGetKind(10, DamageType.Radiation, true, 0, out _).Should().BeFalse();
        PlayerDamageSounds_OnTakeDamage_Patch.TryGetKind(0, DamageType.Normal, true, 0, out _).Should().BeFalse();
    }

    private static PainKind? Kind(float damage, DamageType type, bool underwater, int tanks) =>
        PlayerDamageSounds_OnTakeDamage_Patch.TryGetKind(damage, type, underwater, tanks, out PainKind kind) ? kind : null;
}
