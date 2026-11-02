using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Layers;
using LiteNetLib.Utils;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.MultiplayerSession;
using Nitrox.Model.Networking;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.MultiplayerSession;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Test.Helper.Headless;

/// <summary>
///     A player without a game: speaks the real Nitrox protocol over LiteNetLib (connection key, length-prefixed packets,
///     CRC32C in Release) and records every packet the server sends it. It only does what the test tells it, so it says
///     nothing about what the game would do with those packets.
/// </summary>
/// <remarks>
///     Written against Nitrox.Model only, the same framing as <c>NitroxClient/.../LiteNetLibClient.cs</c>.
/// </remarks>
public sealed class FakePlayer : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly NetManager client;
    private readonly NetDataWriter dataWriter = new();
    private readonly List<Packet> received = [];
    private readonly SemaphoreSlim receivedSignal = new(0);
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; }

    /// <summary>
    ///     What the server sent this player when it joined: escape pod, global root entities, other players, ...
    /// </summary>
    public InitialPlayerSync InitialSync { get; private set; } = null!;

    public SessionId SessionId { get; private set; }

    public bool IsConnected => client.FirstPeer?.ConnectionState == ConnectionState.Connected;

    static FakePlayer()
    {
        // Same as the server and the game do once. Calling it again is harmless (see PacketsSerializableTest).
        Packet.InitSerializer();
    }

    private FakePlayer(string name)
    {
        Name = name;
        EventBasedNetListener listener = new();
        listener.PeerConnectedEvent += _ => connected.TrySetResult();
        listener.PeerDisconnectedEvent += (_, info) => connected.TrySetException(new InvalidOperationException($"{name} was disconnected: {info.Reason}"));
        listener.NetworkReceiveEvent += OnReceive;
        client = new NetManager(listener, NitroxEnvironment.IsReleaseMode ? new Crc32cLayer() : null)
        {
            UpdateTime = 15,
            ChannelsCount = (byte)typeof(Packet.UdpChannelId).GetEnumValues().Length,
            UnsyncedEvents = true,
            DisconnectTimeout = 30_000
        };
    }

    /// <summary>
    ///     Connects and completes the real join handshake: session policy, reservation, join queue, initial sync, sync
    ///     finished. Returns once the server considers the player joined.
    /// </summary>
    public static async Task<FakePlayer> JoinAsync(string name, int port, TimeSpan? timeout = null)
    {
        FakePlayer player = await ConnectAsync(name, port, timeout);
        try
        {
            MultiplayerSessionReservation reservation = await player.ReserveAsync(timeout);
            if (reservation.ReservationState != MultiplayerSessionReservationState.RESERVED)
            {
                throw new InvalidOperationException($"{name}'s reservation was rejected: {reservation.ReservationState} {reservation.RejectionReason}");
            }
            await player.CompleteJoinAsync(timeout);
            return player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     The first part of the join handshake: connects and gets the session policy (and with it the session id).
    ///     Continue with <see cref="ReserveAsync" /> and <see cref="CompleteJoinAsync" />.
    /// </summary>
    public static async Task<FakePlayer> ConnectAsync(string name, int port, TimeSpan? timeout = null)
    {
        TimeSpan wait = timeout ?? DefaultTimeout;
        FakePlayer player = new(name);
        try
        {
            player.client.Start();
            player.client.Connect("127.0.0.1", port, "nitrox");
            await player.connected.Task.WaitAsync(wait);

            player.Send(new MultiplayerSessionPolicyRequest());
            MultiplayerSessionPolicy policy = await player.WaitForAsync<MultiplayerSessionPolicy>(timeout: wait);
            player.SessionId = policy.SessionId;
            return player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    public void SendReservationRequest()
    {
        Send(new MultiplayerSessionReservationRequest(new PlayerSettings(new NitroxColor(1, 1, 1)), new AuthenticationContext(Name, Optional.Empty)));
    }

    /// <summary>
    ///     Asks to reserve <see cref="Name" /> and returns the server's answer, whether accepted or not.
    /// </summary>
    public async Task<MultiplayerSessionReservation> ReserveAsync(TimeSpan? timeout = null)
    {
        SendReservationRequest();
        return await WaitForAsync<MultiplayerSessionReservation>(timeout: timeout ?? DefaultTimeout);
    }

    /// <summary>
    ///     The rest of the join handshake after the reservation: join queue, initial sync, sync finished.
    /// </summary>
    public async Task CompleteJoinAsync(TimeSpan? timeout = null)
    {
        Send(new PlayerJoiningMultiplayerSession());
        InitialSync = await WaitForAsync<InitialPlayerSync>(timeout: timeout ?? DefaultTimeout);
        // Without this the server keeps the join queue blocked until InitialSyncTimeout (JoiningManager)
        Send(new PlayerSyncFinished());
    }

    public void Send(Packet packet)
    {
        byte[] packetData = packet.Serialize();
        lock (dataWriter)
        {
            dataWriter.Reset();
            dataWriter.Put(packetData.Length);
            dataWriter.Put(packetData);
            client.SendToAll(dataWriter, (byte)packet.UdpChannel, NitroxDeliveryMethod.ToLiteNetLib(packet.DeliveryMethod));
        }
    }

    /// <summary>
    ///     Every packet received so far, oldest first.
    /// </summary>
    public List<Packet> Received
    {
        get
        {
            lock (received)
            {
                return [..received];
            }
        }
    }

    public List<T> ReceivedOf<T>() where T : Packet => Received.OfType<T>().ToList();

    /// <summary>
    ///     Waits for a packet of type <typeparamref name="T" /> that matches, including packets received before the call.
    ///     Use <see cref="ClearReceived" /> first to wait only for new ones.
    /// </summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? predicate = null, TimeSpan? timeout = null) where T : Packet
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan wait = timeout ?? DefaultTimeout;
        while (true)
        {
            T? match = Received.OfType<T>().FirstOrDefault(packet => predicate?.Invoke(packet) ?? true);
            if (match != null)
            {
                return match;
            }
            TimeSpan left = wait - stopwatch.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                throw new TimeoutException($"{Name} didn't receive a matching {typeof(T).Name} within {wait.TotalSeconds} s. Received: {string.Join(", ", Received.Select(p => p.GetType().Name))}");
            }
            await receivedSignal.WaitAsync(left);
        }
    }

    /// <summary>
    ///     Fails if a matching packet arrived, or arrives within <paramref name="quietPeriod" />. Prefer asserting on a
    ///     reply that always comes (e.g. the <see cref="SpawnEntities" /> answering a <see cref="CellVisibilityChanged" />),
    ///     a quiet period can only show that nothing came *yet*.
    /// </summary>
    public async Task AssertNotReceivedAsync<T>(Func<T, bool>? predicate = null, TimeSpan? quietPeriod = null) where T : Packet
    {
        await Task.Delay(quietPeriod ?? TimeSpan.FromMilliseconds(500));
        T? match = Received.OfType<T>().FirstOrDefault(packet => predicate?.Invoke(packet) ?? true);
        match.Should().BeNull($"{Name} should not have received this {typeof(T).Name}");
    }

    public void ClearReceived()
    {
        lock (received)
        {
            received.Clear();
        }
    }

    public void Dispose()
    {
        client.Stop();
    }

    /// <summary>
    ///     Like a game that crashed: stops without telling the server, which keeps the session until the connection times
    ///     out.
    /// </summary>
    public void Crash()
    {
        client.Stop(false);
    }

    public override string ToString() => $"FakePlayer {Name}";

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        int packetDataLength = reader.GetInt();
        byte[] packetData = ArrayPool<byte>.Shared.Rent(packetDataLength);
        try
        {
            reader.GetBytes(packetData, packetDataLength);
            Packet? packet = Packet.Deserialize(packetData);
            if (packet == null)
            {
                return;
            }
            lock (received)
            {
                received.Add(packet);
            }
            receivedSignal.Release();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetData, true);
            reader.Recycle();
        }
    }
}
