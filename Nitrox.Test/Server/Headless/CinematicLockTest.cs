using System.Collections.Generic;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     COOP_FEATURES_SPEC R1b (upstream #2859): the cinematic locks a game takes before a hatch, ladder or bed side, and the
///     cinematic call that shows the partner's body in it (L2, real server and protocol). The ids are what the client's
///     BedLockId makes for the two sides of one bed: ids no entity has.
/// </summary>
[TestClass]
[TestCategory("L2")]
public class CinematicLockTest
{
    [TestMethod]
    public async Task BedSides_AreLockedPerSide_AndALateJoinerLearnsWhoLiesWhere()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CinematicLock");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId leftSide = new();
        NitroxId rightSide = new();

        (await RequestAsync(alice, leftSide, SimulationLockType.EXCLUSIVE)).Should().BeTrue("Alice lies down on the left");
        (await RequestAsync(bob, leftSide, SimulationLockType.EXCLUSIVE)).Should().BeFalse("the left side is Alice's");
        (await RequestAsync(bob, rightSide, SimulationLockType.EXCLUSIVE)).Should().BeTrue("the right side is free");

        FakePlayer carol = await server.JoinAsync("Carol");
        carol.InitialSync.InitialSimulationOwnerships.Should().ContainSingle(s => s.Id == leftSide)
             .Which.Should().Match<SimulatedEntity>(s => s.SessionId == alice.SessionId && s.LockType == SimulationLockType.EXCLUSIVE);
        carol.InitialSync.InitialSimulationOwnerships.Should().ContainSingle(s => s.Id == rightSide)
             .Which.SessionId.Should().Be(bob.SessionId);

        // Alice gets up (Bed_ExitInUseMode_Patch): the side is free for the next one, and nobody joining later sees her lie there
        (await RequestAsync(alice, leftSide, SimulationLockType.TRANSIENT)).Should().BeTrue();
        FakePlayer dave = await server.JoinAsync("Dave");
        dave.InitialSync.InitialSimulationOwnerships.Should().NotContain(s => s.Id == leftSide);
        (await RequestAsync(carol, leftSide, SimulationLockType.EXCLUSIVE)).Should().BeTrue("a transient lock gives way to an exclusive request");
    }

    [TestMethod]
    public async Task CinematicCall_ReachesThePartner_WithItsAnimationParameters()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-CinematicCall");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        NitroxId terminalId = new();
        Dictionary<string, bool> parameters = new() { ["first_use"] = true, ["cured"] = false, ["using_tool_first"] = true };

        alice.Send(new PlayerCinematicControllerCall(alice.SessionId, terminalId, 1234, "precursor_deactivate_gun", true, parameters));

        PlayerCinematicControllerCall call = await bob.WaitForAsync<PlayerCinematicControllerCall>(p => p.ControllerID == terminalId);
        call.SessionId.Should().Be(alice.SessionId);
        call.StartPlaying.Should().BeTrue();
        call.AnimationParameters.Should().BeEquivalentTo(parameters);
    }

    private static async Task<bool> RequestAsync(FakePlayer player, NitroxId id, SimulationLockType lockType)
    {
        player.ClearReceived();
        player.Send(new SimulationOwnershipRequest(player.SessionId, id, lockType));
        SimulationOwnershipResponse response = await player.WaitForAsync<SimulationOwnershipResponse>(p => p.Id == id);
        return response.LockAcquired;
    }
}
