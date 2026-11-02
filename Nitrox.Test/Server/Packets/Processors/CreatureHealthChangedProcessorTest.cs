using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

[TestClass]
public class CreatureHealthChangedProcessorTest
{
    [TestMethod]
    public void IsReportFromSimulatingPlayer_SenderHoldsLock_ReturnsTrue()
    {
        Player sender = CreatePlayer(1, 1);

        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), 2500f), sender, sender).Should().BeTrue();
    }

    [TestMethod]
    public void IsReportFromSimulatingPlayer_OtherPlayerHoldsLock_ReturnsFalse()
    {
        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), 2500f), CreatePlayer(2, 2), CreatePlayer(1, 1)).Should().BeFalse();
    }

    [TestMethod]
    public void IsReportFromSimulatingPlayer_NobodyHoldsLock_ReturnsFalse()
    {
        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), 2500f), null, CreatePlayer(1, 1)).Should().BeFalse();
    }

    [TestMethod]
    public void IsReportFromSimulatingPlayer_SamePeerInAnotherSession_ReturnsFalse()
    {
        // Player == compares PeerId: a reconnected player must not pass for its previous session
        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), 2500f), CreatePlayer(1, 1), CreatePlayer(1, 2)).Should().BeFalse();
    }

    [TestMethod]
    public void IsReportFromSimulatingPlayer_PreviousOwnerRightAfterHandOver_ReturnsTrue()
    {
        // MULTINAUGHTICA_SPEC M5 (ported from MultiNaughtica): the report the previous simulating player's throttle held back past the hand-over
        // still carries its last hits
        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), 2500f), CreatePlayer(2, 2), CreatePlayer(1, 1), senderWasRecentOwner: true).Should().BeTrue();
    }

    [TestMethod]
    public void IsReportFromSimulatingPlayer_PreviousOwnerWithInvalidHealth_ReturnsFalse()
    {
        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), 0f), CreatePlayer(2, 2), CreatePlayer(1, 1), senderWasRecentOwner: true).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-1f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    public void IsReportFromSimulatingPlayer_InvalidHealth_ReturnsFalse(float health)
    {
        Player sender = CreatePlayer(1, 1);

        CreatureHealthChangedProcessor.IsReportFromSimulatingPlayer(new CreatureHealthChanged(new NitroxId(), health), sender, sender).Should().BeFalse();
    }

    private static Player CreatePlayer(ushort peerId, ushort sessionId)
    {
        return new Player(peerId, sessionId, $"Player{sessionId}", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, Perms.PLAYER,
                          new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                          new Dictionary<string, PingInstancePreference>(), [], false, true);
    }
}
