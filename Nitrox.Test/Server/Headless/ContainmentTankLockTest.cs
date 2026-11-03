using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC P2 (#2538), server side. Eggs hatch, babies are born and fish grow only on the game holding the
///     simulation lock of their containment tank (<c>WaterParkCreature_BornAsync_Patch.CanBeBorn</c>,
///     <c>CreatureEgg_Hatch_Patch</c>), so a tank must always have a holder while someone is connected: the first player
///     who joins, the partner when that player leaves, and the player who split off a new tank by deconstructing part of
///     a stacked one (<c>WaterParkDeconstructed</c>). The game side is the L3 scenario P2-ContainmentEggs.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class ContainmentTankLockTest
{
    // WaterPark.prefab (InteriorPieceEntity.IsWaterPark)
    private const string WATER_PARK_CLASS_ID = "31662630-7cba-4583-8456-2fa1c4cc31aa";

    [TestMethod]
    public async Task TankLock_GoesToTheFirstPlayer_AndToThePartnerWhenThatPlayerLeaves()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-P2-TankLock");
        BuildEntity baseEntity = CreateBase();
        InteriorPieceEntity tank = CreateTank(baseEntity.Id);
        baseEntity.ChildEntities.Add(tank);
        // The base registers its children; a second AddOrUpdate of the same instance would detach them from it
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(tank, false);

        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.InitialSync.InitialSimulationOwnerships.Should().Contain(e => e.Id == tank.Id && e.SessionId == alice.SessionId, "the first player to join simulates the tank");
        bob.InitialSync.InitialSimulationOwnerships.Should().Contain(e => e.Id == tank.Id && e.SessionId == alice.SessionId, "the partner learns who simulates it");
        server.Get<SimulationOwnershipData>().GetPlayerForLock(tank.Id)!.Name.Should().Be("Alice");

        alice.Dispose();

        await bob.WaitForAsync<SimulationOwnershipChange>(p => p.Entities.Exists(e => e.Id == tank.Id && e.SessionId == bob.SessionId));
        server.Get<SimulationOwnershipData>().GetPlayerForLock(tank.Id)!.Name.Should().Be("Bob", "the tank isn't left without a holder while Bob plays alone");
    }

    [TestMethod]
    public async Task TankSplitOffByDeconstruction_GetsAHolder()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-P2-TankSplit");
        BuildEntity baseEntity = CreateBase();
        InteriorPieceEntity tank = CreateTank(baseEntity.Id);
        GlobalRootEntity egg = new(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, "cutefish-egg-class-id", true, new NitroxId(), new NitroxTechType("CutefishEgg"), null, tank.Id, []);
        tank.ChildEntities.Add(egg);
        baseEntity.ChildEntities.Add(tank);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(baseEntity);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(tank, false);
        server.WorldEntityManager.AddOrUpdateGlobalRootEntity(egg, false);

        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        // Bob deconstructs a segment of a stacked tank: what the game sends (BaseDeconstructable_Deconstruct_Patch) is a ghost
        // in the old tank's place and a new tank that takes over its content
        InteriorPieceEntity newTank = CreateTank(baseEntity.Id);
        GhostEntity ghost = GhostEntity.MakeEmpty();
        ghost.Id = tank.Id;
        ghost.ParentId = baseEntity.Id;
        ghost.Level = GlobalRootEntity.GLOBAL_ROOT_LEVEL;
        ghost.Transform = new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One);
        ghost.TechType = new NitroxTechType("BaseWaterPark");
        BuildPieceIdentifier identifier = new(new NitroxTechType("BaseWaterPark"), new NitroxBaseFace(new NitroxInt3(0, 0, 0), 0), new NitroxInt3(0, 0, 0), new NitroxInt3(0, 0, 0));
        bob.Send(new WaterParkDeconstructed(baseEntity.Id, tank.Id, identifier, ghost, baseEntity.BaseData, newTank, [], true, baseEntity.OperationId + 1));

        await alice.WaitForAsync<WaterParkDeconstructed>(p => p.NewWaterPark.Id == newTank.Id);
        server.EntityRegistry.GetEntityById<GlobalRootEntity>(egg.Id).Value.ParentId.Should().Be(newTank.Id, "the egg is in the new tank");
        await alice.WaitForAsync<SimulationOwnershipChange>(p => p.Entities.Exists(e => e.Id == newTank.Id && e.SessionId == bob.SessionId));
        server.Get<SimulationOwnershipData>().GetPlayerForLock(newTank.Id)!.Name.Should().Be("Bob", "a tank nobody simulates never hatches its eggs");

        bob.Dispose();

        await alice.WaitForAsync<SimulationOwnershipChange>(p => p.Entities.Exists(e => e.Id == newTank.Id && e.SessionId == alice.SessionId));
    }

    private static InteriorPieceEntity CreateTank(NitroxId baseId)
    {
        return new InteriorPieceEntity(new NitroxBaseFace(new NitroxInt3(0, 0, 0), 0), new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One),
                                       GlobalRootEntity.GLOBAL_ROOT_LEVEL, WATER_PARK_CLASS_ID, true, new NitroxId(), new NitroxTechType("BaseWaterPark"), null, baseId, []);
    }

    private static BuildEntity CreateBase()
    {
        BuildEntity baseEntity = BuildEntity.MakeEmpty();
        baseEntity.Id = new NitroxId();
        baseEntity.Level = GlobalRootEntity.GLOBAL_ROOT_LEVEL;
        baseEntity.SpawnedByServer = true;
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
        return baseEntity;
    }
}
