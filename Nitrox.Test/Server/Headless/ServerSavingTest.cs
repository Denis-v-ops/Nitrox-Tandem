using System.Threading.Tasks;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     MULTINAUGHTICA_SPEC M7: the server tells every player when a save starts and when it ends, also when it failed, so
///     the games can show their "Saving game" indicator meanwhile. Only the server sends it. (L2: real server and
///     protocol. What the game shows is checked in game by M7-SavingIndicator.)
/// </summary>
[TestClass]
[TestCategory("L2")]
public class ServerSavingTest
{
    [TestMethod]
    public async Task TheSaveCommand_TellsEveryPlayerItsStartAndItsEnd()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ServerSaving");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");
        alice.ClearReceived();
        bob.ClearReceived();

        await RunCommandAsync(server, "save");

        int aliceSave = await AssertStartedThenEndedAsync(alice, ServerSaving.SavePhase.FINISHED);
        int bobSave = await AssertStartedThenEndedAsync(bob, ServerSaving.SavePhase.FINISHED);
        bobSave.Should().Be(aliceSave, "both players are told about the same save");
        File.Exists(Path.Combine(server.SavePath, "Version.json")).Should().BeTrue();
    }

    [TestMethod]
    public async Task AFailedSave_StillTellsThePlayersThatItEnded()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ServerSavingFails");
        FakePlayer alice = await server.JoinAsync("Alice");
        alice.ClearReceived();
        // The serializer writes Version.tmp first (then replaces Version.json): a folder in its place makes the save fail
        string blocker = Path.Combine(server.SavePath, "Version.tmp");
        Directory.CreateDirectory(blocker);

        await RunCommandAsync(server, "save");
        await AssertStartedThenEndedAsync(alice, ServerSaving.SavePhase.FAILED);

        // The next save works again
        Directory.Delete(blocker);
        alice.ClearReceived();
        await RunCommandAsync(server, "save");
        await AssertStartedThenEndedAsync(alice, ServerSaving.SavePhase.FINISHED);
    }

    [TestMethod]
    public async Task APlayerCantSendIt_ToTheOthers()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-ServerSavingRelay");
        FakePlayer alice = await server.JoinAsync("Alice");
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new ServerSaving(99, ServerSaving.SavePhase.STARTED));

        await bob.AssertNotReceivedAsync<ServerSaving>(packet => packet.Number == 99, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    ///     Like <c>save</c> typed into the server console (or <c>bridge.ps1 server save</c>). The command only queues the save.
    /// </summary>
    private static async Task RunCommandAsync(ServerHarness server, string command)
    {
        CommandService commands = server.Get<CommandService>();
        commands.ExecuteCommand(command, new HostToServerCommandContext(server.Get<IPacketSender>()), out Task<bool>? task).Should().BeTrue();
        if (task != null)
        {
            (await task).Should().BeTrue();
        }
    }

    /// <summary>
    ///     Waits for the end of a save that the player was told had started, and returns its number. The server also saves
    ///     once by itself after it starts (it starts asleep, <c>HibernateService</c>), so only a matched pair counts.
    /// </summary>
    private static async Task<int> AssertStartedThenEndedAsync(FakePlayer player, ServerSaving.SavePhase expectedEnd)
    {
        ServerSaving end = await player.WaitForAsync<ServerSaving>(packet => packet.Phase != ServerSaving.SavePhase.STARTED && HasStarted(player, packet.Number));
        end.Phase.Should().Be(expectedEnd);
        List<ServerSaving> received = player.ReceivedOf<ServerSaving>();
        received.FindIndex(packet => packet.Number == end.Number && packet.Phase == ServerSaving.SavePhase.STARTED)
                .Should().BeLessThan(received.FindIndex(packet => packet.Number == end.Number && packet.Phase == expectedEnd), "the start comes first");
        return end.Number;
    }

    private static bool HasStarted(FakePlayer player, int number) => player.ReceivedOf<ServerSaving>().Any(packet => packet.Number == number && packet.Phase == ServerSaving.SavePhase.STARTED);
}
