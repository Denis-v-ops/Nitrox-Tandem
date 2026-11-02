using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Bases;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.GameLogic.Bases;

/// <summary>
///     MULTINAUGHTICA_SPEC M1: a loose <c>Constructable</c> in the world (not a ghost or module, e.g. a cargo crate) that is
///     fully deconstructed is removed like any destroyed entity: registry, cell entry and the deconstructing player's lock.
/// </summary>
[TestClass]
public class BuildingManagerDeconstructTest
{
    private static readonly NitroxVector3 position = new(10, -20, 10);

    private EntityRegistry entityRegistry;
    private WorldEntityManager worldEntityManager;
    private SimulationOwnershipData simulationOwnershipData;
    private BuildingManager buildingManager;
    private ServerPlayer player;

    [TestInitialize]
    public void Setup()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        entityRegistry = new EntityRegistry(NullLogger<EntityRegistry>.Instance);
        PlayerManager playerManager = new(null, null, null, NullLogger<PlayerManager>.Instance);
        worldEntityManager = new WorldEntityManager(packetSender, entityRegistry, null, playerManager, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        simulationOwnershipData = new SimulationOwnershipData();
        buildingManager = new BuildingManager(packetSender, entityRegistry, worldEntityManager, simulationOwnershipData, Options.Create(new SubnauticaServerOptions()), NullLogger<BuildingManager>.Instance);
        player = new ServerPlayer(default, default, "Tester", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, default, null, default,
                                  [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(), new Dictionary<string, PingInstancePreference>(), [], false, false);
    }

    [TestMethod]
    public void FullyDeconstructedLooseConstructable_LeavesRegistryCellAndLock()
    {
        WorldEntity crate = AddLooseConstructable();
        simulationOwnershipData.TryToAcquire(crate.Id, player, SimulationLockType.TRANSIENT).Should().BeTrue();

        buildingManager.ModifyConstructedAmount(player, new ModifyConstructedAmount(crate.Id, 0f)).Should().BeTrue("the packet is relayed to the other players");

        entityRegistry.GetEntityById(crate.Id).HasValue.Should().BeFalse();
        IsInCell(crate.Id).Should().BeFalse("players loading the cell would get a removed entity back (GetEntities only drops it with a warning)");
        simulationOwnershipData.TryGetLock(crate.Id, out _).Should().BeFalse("the lock must not outlive the entity");
    }

    [TestMethod]
    public void PartlyDeconstructedLooseConstructable_StaysWhereItIs()
    {
        WorldEntity crate = AddLooseConstructable();

        buildingManager.ModifyConstructedAmount(player, new ModifyConstructedAmount(crate.Id, 0.5f)).Should().BeTrue();

        entityRegistry.GetEntityById(crate.Id).HasValue.Should().BeTrue();
        IsInCell(crate.Id).Should().BeTrue();
    }

    private WorldEntity AddLooseConstructable()
    {
        NitroxTransform transform = new(position, NitroxQuaternion.Identity, NitroxVector3.One);
        WorldEntity crate = new(transform, 0, "cargo-crate-class-id", false, new NitroxId(), new NitroxTechType("StarshipCargoCrate"), null, null, []);
        entityRegistry.AddOrUpdate(crate);
        worldEntityManager.TrackEntityInTheWorld(crate);
        IsInCell(crate.Id).Should().BeTrue("the setup must put it in its cell");
        return crate;
    }

    private bool IsInCell(NitroxId id)
    {
        return worldEntityManager.worldEntitiesByCell.TryGetValue(new AbsoluteEntityCell(position, 0), out Dictionary<NitroxId, WorldEntity> entities) && entities.ContainsKey(id);
    }
}
