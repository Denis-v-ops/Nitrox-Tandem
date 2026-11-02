using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Models.Packets.Processors;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;
using ServerPlayer = Nitrox.Server.Subnautica.Models.Player;

namespace Nitrox.Test.Server.Packets;

/// <summary>
///     FIX_SPEC S10 step 4 (#2046, #2151): scanner rooms keep the resources of cells that aren't loaded, so a destroyed
///     outcrop must reach players who don't see its cell too.
/// </summary>
[TestClass]
public class EntityDestroyedPacketProcessorTest
{
    [TestMethod]
    public async Task DestroyedWorldEntity_IsSentToEveryOtherPlayer_EvenWithoutTheCellLoaded()
    {
        IPacketSender packetSender = Substitute.For<IPacketSender>();
        EntityRegistry entityRegistry = new(NullLogger<EntityRegistry>.Instance);
        PlayerManager playerManager = new(null, null, null, NullLogger<PlayerManager>.Instance);
        WorldEntityManager worldEntityManager = new(packetSender, entityRegistry, null, playerManager, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        EntitySimulation entitySimulation = new(packetSender, entityRegistry, worldEntityManager, new SimulationOwnershipData(), playerManager, NullLogger<EntitySimulation>.Instance);
        ServerPlayer breaker = new(default, default, "Breaker", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, new NitroxId(), Optional.Empty, default, null, default,
                                   [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(), new Dictionary<string, PingInstancePreference>(), [], false, false);

        WorldEntity outcrop = new(new NitroxTransform(new NitroxVector3(600, -20, 600), NitroxQuaternion.Identity, NitroxVector3.One), 0, "limestone-class-id", false, new NitroxId(),
                                  new NitroxTechType("LimestoneChunk"), null, null, []);
        entityRegistry.AddOrUpdate(outcrop);
        worldEntityManager.TrackEntityInTheWorld(outcrop);
        EntityDestroyed packet = new(outcrop.Id);

        await new EntityDestroyedPacketProcessor(entitySimulation, worldEntityManager).Process(new AuthProcessorContext(breaker, packetSender), packet);

        entityRegistry.TryGetEntityById(outcrop.Id, out Entity _).Should().BeFalse();
        await packetSender.Received(1).SendPacketToOthersAsync(packet, breaker.SessionId);
    }
}
