using System;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC F2: the server relays a context ping with its kind and target to the partner, and nothing back to
///     the sender (L2, real server and protocol).
/// </summary>
[TestClass]
[TestCategory("L2")]
public class ContextPingTest
{
    [TestMethod]
    public async Task PingKindAndTargetReachThePartner_NotTheSender()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ContextPing");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        NitroxVector3 position = new(52, -31, 47);
        NitroxId dangerId = new();
        NitroxId lookId = new();
        alice.Send(new PlayerPingCreated(alice.SessionId, PingKind.Danger, new NitroxTechType("Stalker"), position, dangerId));
        alice.Send(new PlayerPingCreated(alice.SessionId, PingKind.Look, new NitroxTechType("Titanium"), position, lookId));

        PlayerPingCreated danger = await bob.WaitForAsync<PlayerPingCreated>(ping => ping.PingId == dangerId);
        danger.SessionId.Should().Be(alice.SessionId);
        danger.Kind.Should().Be(PingKind.Danger);
        danger.TargetTechType.Should().Be(new NitroxTechType("Stalker"));
        danger.Position.Should().Be(position);
        PlayerPingCreated look = await bob.WaitForAsync<PlayerPingCreated>(ping => ping.PingId == lookId);
        look.Kind.Should().Be(PingKind.Look);
        look.TargetTechType.Should().Be(new NitroxTechType("Titanium"));

        // A reply that always comes, sent after the pings: once it's here, the server has processed both pings.
        AbsoluteEntityCell cell = new(position, 0);
        alice.Send(new CellVisibilityChanged(alice.SessionId, [cell], []));
        await alice.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        await alice.AssertNotReceivedAsync<PlayerPingCreated>(quietPeriod: TimeSpan.FromMilliseconds(300));
    }
}
