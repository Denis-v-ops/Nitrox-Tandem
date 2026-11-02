using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using NSubstitute;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.GameLogic;

/// <summary>
///     FIX_SPEC S6: a teleport skips the precursor trigger volumes that set Player.precursorOutOfWater, so the server sends
///     the destination's value with it (warp to a partner standing in a dry precursor room) and <c>back</c> restores the old one.
/// </summary>
[TestClass]
public class PlayerTeleportTest
{
    private static readonly NitroxVector3 dryPrecursorRoom = new(226, -1451, -280);
    private static readonly NitroxVector3 openWater = new(263, -55, 64);

    private IPacketSender packetSender;

    [TestInitialize]
    public void Setup()
    {
        packetSender = Substitute.For<IPacketSender>();
    }

    [TestMethod]
    public void Teleport_SendsAndStoresTheDestinationsInPrecursor()
    {
        ServerPlayer player = CreatePlayer(openWater, Optional.Empty, false);

        player.Teleport(dryPrecursorRoom, Optional.Empty, true, packetSender);

        player.InPrecursor.Should().BeTrue();
        player.Position.Should().Be(dryPrecursorRoom);
        packetSender.Received(1).SendPacketAsync(Arg.Is<PlayerTeleported>(p => p.InPrecursor && p.DestinationTo == dryPrecursorRoom && p.DestinationFrom == openWater), player.SessionId);
    }

    [TestMethod]
    public void Teleport_RemembersWhereThePlayerCameFrom()
    {
        NitroxId cyclopsId = new();
        NitroxId otherSubId = new();
        ServerPlayer player = CreatePlayer(openWater, Optional.Of(cyclopsId), false);

        player.Teleport(dryPrecursorRoom, Optional.Of(otherSubId), true, packetSender);

        player.LastStoredPosition.Should().Be(openWater);
        player.LastStoredSubRootID.Should().Be(Optional.Of(cyclopsId));
        player.LastStoredInPrecursor.Should().BeFalse();
    }

    [TestMethod]
    public void TeleportBack_RestoresTheOldInPrecursor()
    {
        ServerPlayer player = CreatePlayer(dryPrecursorRoom, Optional.Empty, true);
        player.Teleport(openWater, Optional.Empty, false, packetSender);
        player.InPrecursor.Should().BeFalse();

        // What BackCommand does.
        player.Teleport(player.LastStoredPosition!.Value, player.LastStoredSubRootID, player.LastStoredInPrecursor, packetSender);

        player.InPrecursor.Should().BeTrue();
        player.Position.Should().Be(dryPrecursorRoom);
        packetSender.Received(1).SendPacketAsync(Arg.Is<PlayerTeleported>(p => p.InPrecursor && p.DestinationTo == dryPrecursorRoom), player.SessionId);
    }

    private static ServerPlayer CreatePlayer(NitroxVector3 position, Optional<NitroxId> subRootId, bool inPrecursor)
    {
        return new ServerPlayer(default, default, "Tester", false, null, position, NitroxQuaternion.Identity, new NitroxId(), subRootId, default, null, default,
                                [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(), new Dictionary<string, PingInstancePreference>(), [], inPrecursor, false);
    }
}
