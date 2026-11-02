using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC B4: an Aurora fire one player puts out stays out for the partner, for a player who joins later
///     and after a restart (L2). The fires are world entities from the game's batch files
///     (<c>Aurora_ExtinguishableFire_*</c>, the <c>Fire</c> component sits on their child), so the client's
///     <see cref="FireDoused" /> carries the id of such a batch entity.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class AuroraFireTest
{
    /// <summary>Class ids of the Aurora fire prefabs, from the game's <c>SNUnmanagedData/prefabs.db</c>.</summary>
    private static readonly HashSet<string> auroraFireClassIds =
    [
        "3877d31d-37a5-4c94-8eef-881a500c58bc", // Aurora_ExtinguishableFire_Medium
        "afe53ea1-d2a8-4f76-8ffb-d41ff6046b52", // Aurora_ExtinguishableFire_Medium_Tall
        "14bbf7f0-4276-48bf-868b-317b366edd16" // Aurora_ExtinguishableFire_Small
    ];

    /// <summary>The Aurora's cargo room, where the A0 probe found 33 fires.</summary>
    private static readonly NitroxVector3 cargoRoom = new(940, 14, 18);

    [TestMethod]
    public async Task DousedAuroraFire_IsGoneForPartner_LateJoiner_AndAfterRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-AuroraFire");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        WorldEntity fire = await FindAuroraFireAsync(server);
        WorldEntity untouchedFire = (await FindAuroraFiresAsync(server)).First(other => !other.Id.Equals(fire.Id));
        AbsoluteEntityCell cell = fire.AbsoluteEntityCell;
        (await RequestCellAsync(bob, cell)).Should().Contain(entity => entity.Id.Equals(fire.Id), "Bob loads the burning fire first");

        // What Fires.OnDouse sends once Fire.Douse brought the fire's health to 0
        alice.Send(new FireDoused(fire.Id, 0f, null, 0f, true));
        FireDoused relayed = await bob.WaitForAsync<FireDoused>(packet => packet.Id.Equals(fire.Id));
        relayed.Health.Should().Be(0f);
        server.EntityRegistry.TryGetEntityById(fire.Id, out Entity _).Should().BeFalse("the server forgets the doused fire");
        server.IsInCell(fire.Id, cell).Should().BeFalse("the fire's cell no longer lists it");

        FakePlayer carol = await server.JoinAsync("Carol");
        (await RequestCellAsync(carol, cell)).Should().NotContain(entity => entity.Id.Equals(fire.Id), "a player who joins later doesn't get the fire");

        await server.RestartAsync();

        server.EntityRegistry.TryGetEntityById(fire.Id, out Entity _).Should().BeFalse("the doused fire isn't regenerated from the batch on load");
        server.EntityRegistry.TryGetEntityById(untouchedFire.Id, out WorldEntity _).Should().BeTrue("the other fires are still saved");
        FakePlayer bobAgain = await server.JoinAsync("Bob");
        List<Entity> reloaded = await RequestCellAsync(bobAgain, untouchedFire.AbsoluteEntityCell);
        reloaded.Should().Contain(entity => entity.Id.Equals(untouchedFire.Id), "the fires nobody doused still burn");
        (await RequestCellAsync(bobAgain, cell)).Should().NotContain(entity => entity.Id.Equals(fire.Id), "the doused fire stays out after the restart");
    }

    private static async Task<WorldEntity> FindAuroraFireAsync(ServerHarness server) => (await FindAuroraFiresAsync(server)).First();

    private static async Task<List<WorldEntity>> FindAuroraFiresAsync(ServerHarness server)
    {
        await server.WorldEntityManager.LoadUnspawnedEntitiesAsync(new AbsoluteEntityCell(cargoRoom, 0).BatchId, true);
        List<WorldEntity> fires = server.EntityRegistry.GetEntities<WorldEntity>()
                                        .Where(entity => auroraFireClassIds.Contains(entity.ClassId))
                                        .OrderBy(entity => NitroxVector3.Distance(entity.Transform.Position, cargoRoom))
                                        .ToList();
        fires.Count.Should().BeGreaterThan(1, "the Aurora's batch has fires");
        return fires;
    }

    /// <summary>Every entity, nested children included, the server sends when the player loads the cell.</summary>
    private static async Task<List<Entity>> RequestCellAsync(FakePlayer player, AbsoluteEntityCell cell)
    {
        player.ClearReceived();
        player.Send(new CellVisibilityChanged(player.SessionId, [cell], []));
        SpawnEntities reply = await player.WaitForAsync<SpawnEntities>(packet => packet.SpawnedCells.Contains(cell));
        List<Entity> all = [];
        Stack<Entity> pending = new(reply.Entities);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            all.Add(entity);
            entity.ChildEntities.ForEach(pending.Push);
        }
        return all;
    }
}
