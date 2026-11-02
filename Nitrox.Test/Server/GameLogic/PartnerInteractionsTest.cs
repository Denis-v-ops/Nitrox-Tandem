using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Processors;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.GameLogic;

/// <summary>
///     COOP_FEATURES_SPEC T2: the partner-interaction check (buddy breathing now, revives and giving later) and the buddy
///     breathing amount (L1).
/// </summary>
[TestClass]
public class PartnerInteractionsTest
{
    private static readonly NitroxVector3 spot = new(-60, -6, 380);

    [TestMethod]
    public void Check_AllowsTwoOnlinePlayersCloseTogether()
    {
        ServerPlayer alice = CreatePlayer(1, spot);
        ServerPlayer bob = CreatePlayer(2, spot + new NitroxVector3(1.5f, 0, 0));

        PartnerInteractions.Check(alice, bob, PartnerInteractionRules.None).Should().Be(PartnerInteractionResult.Allowed);
    }

    [TestMethod]
    public void Check_RefusesBeyondReach()
    {
        ServerPlayer alice = CreatePlayer(1, spot);

        PartnerInteractions.Check(alice, CreatePlayer(2, spot + new NitroxVector3(0, 0, PartnerInteractions.MAX_DISTANCE)), PartnerInteractionRules.None)
                           .Should().Be(PartnerInteractionResult.Allowed);
        PartnerInteractions.Check(alice, CreatePlayer(2, spot + new NitroxVector3(0, 0, PartnerInteractions.MAX_DISTANCE + 0.1f)), PartnerInteractionRules.None)
                           .Should().Be(PartnerInteractionResult.TooFar);
    }

    [TestMethod]
    public void Check_RefusesOfflineMissingOrSamePlayer()
    {
        ServerPlayer alice = CreatePlayer(1, spot);
        ServerPlayer offline = CreatePlayer(2, spot);
        offline.IsOnline = false;

        PartnerInteractions.Check(alice, offline, PartnerInteractionRules.None).Should().Be(PartnerInteractionResult.TargetOffline);
        PartnerInteractions.Check(alice, null, PartnerInteractionRules.None).Should().Be(PartnerInteractionResult.TargetOffline);
        PartnerInteractions.Check(offline, alice, PartnerInteractionRules.None).Should().Be(PartnerInteractionResult.SenderOffline);
        PartnerInteractions.Check(alice, alice, PartnerInteractionRules.None).Should().Be(PartnerInteractionResult.SameAsSender);
    }

    [TestMethod]
    public void Check_DownedPlayersOnlyWhereTheActionAllowsIt()
    {
        ServerPlayer alice = CreatePlayer(1, spot);
        ServerPlayer bob = CreatePlayer(2, spot);
        bob.DownedUntil = 100;

        PartnerInteractions.Check(alice, bob, PartnerInteractionRules.None).Should().Be(PartnerInteractionResult.TargetDowned);
        PartnerInteractions.Check(alice, bob, PartnerInteractionRules.TargetMayBeDowned).Should().Be(PartnerInteractionResult.Allowed);
        PartnerInteractions.Check(bob, alice, PartnerInteractionRules.TargetMayBeDowned).Should().Be(PartnerInteractionResult.SenderDowned);
        PartnerInteractions.Check(bob, alice, PartnerInteractionRules.SenderMayBeDowned).Should().Be(PartnerInteractionResult.Allowed);
    }

    [TestMethod]
    public void AmountReceived_IsClampedAndScaled()
    {
        OxygenShareProcessor.AmountReceived(1f, 0.5f).Should().Be(0.5f);
        OxygenShareProcessor.AmountReceived(0.4f, 0.5f).Should().Be(0.2f);
        OxygenShareProcessor.AmountReceived(50f, 0.5f).Should().Be(OxygenShare.MAX_AMOUNT * 0.5f, "one packet carries at most MAX_AMOUNT");
        OxygenShareProcessor.AmountReceived(1f, 1f).Should().Be(1f);
        OxygenShareProcessor.AmountReceived(1f, 3f).Should().Be(1f, "sharing never creates air");
        OxygenShareProcessor.AmountReceived(1f, 0f).Should().Be(0f);
        OxygenShareProcessor.AmountReceived(-1f, 0.5f).Should().Be(0f);
        OxygenShareProcessor.AmountReceived(float.NaN, 0.5f).Should().Be(0f);
        OxygenShareProcessor.AmountReceived(1f, float.NaN).Should().Be(0f);
    }

    private static ServerPlayer CreatePlayer(ushort sessionId, NitroxVector3 position)
    {
        return new ServerPlayer(default, sessionId, $"Player{sessionId}", false, null, position, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, default, null, default,
                                [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(), new Dictionary<string, PingInstancePreference>(), [], false, false);
    }
}
