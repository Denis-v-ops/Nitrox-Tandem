using System.Threading.Tasks;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M8, crafting from base and Cyclops lockers: the server half over the real protocol. A claim for
///     locker items is granted to one player only, the removals reach both games before the answer, the result survives a
///     restart, a granted claim given back puts the same items back once, and with <c>CraftFromLockers</c> off nothing is
///     taken.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class LockerCraftingTest
{
    [TestMethod]
    public async Task TwoPlayersClaimTheLastItem_OneIsGranted_AndItStaysGoneAfterRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M8-Race");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        alice.InitialSync.CoopSettings.CraftFromLockers.Should().BeTrue("the option is on by default and reaches the game");
        (BuildEntity baseEntity, ModuleEntity locker) = AddBaseWithLocker(server);
        InventoryItemEntity lastTitanium = AddItem(locker, "Titanium");
        InventoryItemEntity copper = AddItem(locker, "Copper");
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);
        alice.ClearReceived();
        bob.ClearReceived();

        alice.Send(new CraftingClaimRequest(1, baseEntity.Id, [lastTitanium.Id]));
        bob.Send(new CraftingClaimRequest(1, baseEntity.Id, [lastTitanium.Id]));

        CraftingClaimResponse aliceAnswer = await alice.WaitForAsync<CraftingClaimResponse>(response => response.ClaimId == 1);
        CraftingClaimResponse bobAnswer = await bob.WaitForAsync<CraftingClaimResponse>(response => response.ClaimId == 1);
        CraftingClaimOutcome[] outcomes = [aliceAnswer.Outcome, bobAnswer.Outcome];
        outcomes.Order().Should().Equal([CraftingClaimOutcome.Granted, CraftingClaimOutcome.Taken], "exactly one player gets the last titanium");
        (FakePlayer winner, FakePlayer loser) = aliceAnswer.Outcome == CraftingClaimOutcome.Granted ? (alice, bob) : (bob, alice);
        CraftingClaimResponse refusal = loser == alice ? aliceAnswer : bobAnswer;
        refusal.UnavailableIds.Should().Equal(lastTitanium.Id);
        foreach (FakePlayer player in new[] { winner, loser })
        {
            player.ReceivedOf<EntityDestroyed>().Where(packet => packet.Id == lastTitanium.Id).Should().ContainSingle($"{player.Name}'s locker loses the titanium once");
        }
        List<Packet> loserPackets = loser.Received;
        loserPackets.FindIndex(packet => packet is EntityDestroyed destroyed && destroyed.Id == lastTitanium.Id).Should()
                    .BeLessThan(loserPackets.IndexOf(refusal), "the loser hears of the removal before its refusal, so its retry sees the locker without it");
        AssertLockerHolds(server, locker.Id, copper.Id);

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(lastTitanium.Id, out Entity _).Should().BeFalse("the claimed titanium doesn't come back after a restart");
        AssertLockerHolds(server, locker.Id, copper.Id);
    }

    [TestMethod]
    public async Task AGrantedClaimGivenBack_PutsTheSameItemsBackForEveryone_Once()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M8-Return");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        (BuildEntity baseEntity, ModuleEntity locker) = AddBaseWithLocker(server);
        InventoryItemEntity battery = new(new NitroxId(), "battery-class-id", new NitroxTechType("Battery"), new BatteryMetadata(12), locker.Id, []);
        locker.ChildEntities.Add(battery);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);

        alice.Send(new CraftingClaimRequest(5, baseEntity.Id, [battery.Id]));
        (await alice.WaitForAsync<CraftingClaimResponse>(response => response.ClaimId == 5)).Outcome.Should().Be(CraftingClaimOutcome.Granted);
        await bob.WaitForAsync<EntityDestroyed>(packet => packet.Id == battery.Id);
        bob.ClearReceived();

        alice.Send(new CraftingClaimReturn(5));

        foreach (FakePlayer player in new[] { alice, bob })
        {
            SpawnEntities spawn = await player.WaitForAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == battery.Id));
            Entity back = spawn.Entities.Single(entity => entity.Id == battery.Id);
            back.ParentId.Should().Be(locker.Id, $"{player.Name}'s game puts it back into the locker");
            back.Metadata.Should().BeOfType<BatteryMetadata>().Which.Charge.Should().Be(12, "the same item comes back, not a fresh one");
        }
        AssertLockerHolds(server, locker.Id, battery.Id);

        // A second return (a bug, a resend) must not make a second battery
        bob.ClearReceived();
        alice.Send(new CraftingClaimReturn(5));
        bob.Send(new CraftingClaimReturn(5));
        await bob.AssertNotReceivedAsync<SpawnEntities>(packet => packet.Entities.Any(entity => entity.Id == battery.Id));
        AssertLockerHolds(server, locker.Id, battery.Id);

        await server.RestartAsync();

        AssertLockerHolds(server, locker.Id, battery.Id);
    }

    [TestMethod]
    public async Task WithTheOptionOff_AClaimIsRefused_AndNothingIsTaken()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M8-Off", [$"--{SubnauticaServerOptions.CONFIG_SECTION_PATH}:{nameof(SubnauticaServerOptions.CraftFromLockers)}", "false"]);
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        alice.InitialSync.CoopSettings.CraftFromLockers.Should().BeFalse("the game learns the option at join");
        (BuildEntity baseEntity, ModuleEntity locker) = AddBaseWithLocker(server);
        InventoryItemEntity titanium = AddItem(locker, "Titanium");
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);

        // A game that ignores the option (or an old one) still gets nothing
        alice.Send(new CraftingClaimRequest(1, baseEntity.Id, [titanium.Id]));

        (await alice.WaitForAsync<CraftingClaimResponse>(response => response.ClaimId == 1)).Outcome.Should().Be(CraftingClaimOutcome.Disabled);
        alice.ReceivedOf<EntityDestroyed>().Should().NotContain(packet => packet.Id == titanium.Id);
        bob.ReceivedOf<EntityDestroyed>().Should().NotContain(packet => packet.Id == titanium.Id);
        AssertLockerHolds(server, locker.Id, titanium.Id);
    }

    [TestMethod]
    public async Task AnAnswerSentByAGame_IsNotRelayedToThePartner()
    {
        // The answer is the server's alone: a relayed grant would start the partner's craft without the items being taken
        await using ServerHarness server = await ServerHarness.StartAsync("L2-M8-Relay");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        bob.ClearReceived();

        alice.Send(new CraftingClaimResponse(1, CraftingClaimOutcome.Granted, []));
        // A packet the server does relay, sent after it: once it arrives, a relayed answer would have arrived too
        alice.Send(new PlayerStats(alice.SessionId, 45, 45, 80, 50, 90, 0.1f, false));

        await bob.WaitForAsync<PlayerStats>(stats => stats.SessionId == alice.SessionId);
        bob.ReceivedOf<CraftingClaimResponse>().Should().BeEmpty();
    }

    private static (BuildEntity BaseEntity, ModuleEntity Locker) AddBaseWithLocker(ServerHarness server)
    {
        BuildEntity baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        baseEntity.Level = GlobalRootEntity.GLOBAL_ROOT_LEVEL;
        baseEntity.SpawnedByServer = true;
        baseEntity.TechType = new NitroxTechType("BaseRoom");
        baseEntity.Transform = new NitroxTransform(new NitroxVector3(60, -26, 207), NitroxQuaternion.Identity, NitroxVector3.One);
        baseEntity.BaseData = new BaseData
        {
            BaseShape = new NitroxInt3(1, 1, 1),
            CellOffset = new NitroxInt3(0, 0, 0),
            Anchor = new NitroxInt3(0, 0, 0),
            PreCompressionSize = 1,
            Faces = [1],
            Cells = [2],
            Links = [3],
            Masks = [4],
            IsGlass = [0]
        };
        ModuleEntity locker = new(1, true, new NitroxTransform(new NitroxVector3(1, 0, 1), NitroxQuaternion.Identity, NitroxVector3.One), GlobalRootEntity.GLOBAL_ROOT_LEVEL, "locker-class-id", true,
                                  new NitroxId(), new NitroxTechType("Locker"), null, baseEntity.Id, []);
        baseEntity.ChildEntities.Add(locker);
        return (baseEntity, locker);
    }

    private static InventoryItemEntity AddItem(ModuleEntity locker, string techType)
    {
        InventoryItemEntity item = new(new NitroxId(), $"{techType}-class-id", new NitroxTechType(techType), null, locker.Id, []);
        locker.ChildEntities.Add(item);
        return item;
    }

    private static void AssertLockerHolds(ServerHarness server, NitroxId lockerId, params NitroxId[] itemIds)
    {
        server.EntityRegistry.TryGetEntityById(lockerId, out ModuleEntity locker).Should().BeTrue();
        locker.ChildEntities.Select(child => child.Id).Should().BeEquivalentTo(itemIds);
        foreach (NitroxId itemId in itemIds)
        {
            server.EntityRegistry.TryGetEntityById(itemId, out InventoryItemEntity item).Should().BeTrue();
            item.ParentId.Should().Be(lockerId);
        }
    }
}
