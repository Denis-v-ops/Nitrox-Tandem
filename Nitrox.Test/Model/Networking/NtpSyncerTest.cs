using LiteNetLib;

namespace Nitrox.Model.Networking;

/// <summary>
///     MULTINAUGHTICA_SPEC M3: every NTP round opens a socket (a LiteNetLib NetManager) and must close it. A game without an
///     NTP correction starts a round for every TimeChange, the server one every minute while it's offline. Real sockets on
///     free local ports; no request goes out.
/// </summary>
[TestClass]
public class NtpSyncerTest
{
    [TestMethod]
    public void Setup_StopsTheRoundBefore()
    {
        NtpSyncer syncer = new();
        syncer.Setup();
        NetManager first = syncer.CurrentNetManager!;
        first.IsRunning.Should().BeTrue();

        syncer.Setup();

        first.IsRunning.Should().BeFalse("the first round's socket is closed");
        syncer.CurrentNetManager!.IsRunning.Should().BeTrue();
        syncer.IsRunning.Should().BeTrue();
        syncer.Complete();
    }

    [TestMethod]
    public void ARoundAfterACompletedOne_ClosesItsSocketAndReports()
    {
        NtpSyncer syncer = new();
        syncer.Setup();
        syncer.Complete();
        bool reported = false;

        syncer.Setup((_, _) => reported = true);
        NetManager second = syncer.CurrentNetManager!;
        syncer.IsComplete.Should().BeFalse();
        syncer.Complete();

        second.IsRunning.Should().BeFalse("the second round's socket is closed");
        reported.Should().BeTrue("the second round's caller learns the result (the server's retry timer stops on it)");
        syncer.IsComplete.Should().BeTrue();
        syncer.IsRunning.Should().BeFalse();
    }
}
