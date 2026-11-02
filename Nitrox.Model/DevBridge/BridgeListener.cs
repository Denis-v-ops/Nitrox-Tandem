#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nitrox.Model.Helper;

namespace Nitrox.Model.DevBridge;

/// <summary>
///     Debug-only endpoint that lets agents drive a game or the server from the shell (<c>tools/bridge.ps1</c>).
///     Listens on 127.0.0.1 only. One request per connection: a line of text in, the reply out, then the connection is
///     closed. Replies that start with <see cref="ERROR_PREFIX" /> are errors.
/// </summary>
/// <remarks>
///     The port is derived from the name (so it is stable across runs) and written, with the process id, to
///     <c>%APPDATA%\Nitrox\bridge\&lt;name&gt;.json</c>, which is what <c>bridge.ps1</c> reads.
/// </remarks>
public sealed class BridgeListener : IDisposable
{
    public const string ERROR_PREFIX = "ERR ";
    private const int BASE_PORT = 21000;
    private const int PORT_RANGE = 1000;

    private readonly Func<string, Task<string>> handleRequest;
    private readonly string infoFile;
    private readonly TcpListener listener;
    private volatile bool running = true;

    public static string InfoDirectory => Path.Combine(NitroxDirectory.ConfigPath, "bridge");

    public string Name { get; }
    public int Port { get; }

    public BridgeListener(string name, Func<string, Task<string>> handleRequest)
    {
        Name = name;
        this.handleRequest = handleRequest;
        listener = Bind(GetPreferredPort(name));
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Directory.CreateDirectory(InfoDirectory);
        infoFile = Path.Combine(InfoDirectory, $"{ToFileName(name)}.json");
        File.WriteAllText(infoFile, $"{{\"name\":{BridgeJson.Serialize(name)},\"port\":{Port},\"pid\":{Process.GetCurrentProcess().Id}}}");

        Thread thread = new(AcceptLoop) { IsBackground = true, Name = $"Nitrox dev bridge ({name})" };
        thread.Start();
    }

    /// <summary>
    ///     Stable port for a name: 21000 + FNV-1a hash of the lower-case name modulo 1000.
    /// </summary>
    public static int GetPreferredPort(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name.ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619;
        }
        return BASE_PORT + (int)(hash % PORT_RANGE);
    }

    public static string ToFileName(string name) => new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    public void Dispose()
    {
        running = false;
        listener.Stop();
        try
        {
            File.Delete(infoFile);
        }
        catch (Exception)
        {
            // Stale info files are harmless: bridge.ps1 reports that nothing listens on the port.
        }
    }

    private static TcpListener Bind(int preferredPort)
    {
        for (int port = preferredPort; port < preferredPort + 20; port++)
        {
            TcpListener candidate = new(IPAddress.Loopback, port);
            try
            {
                candidate.Start();
                return candidate;
            }
            catch (SocketException)
            {
                // Port taken, for example by another game instance whose name hashes close to ours.
            }
        }
        TcpListener any = new(IPAddress.Loopback, 0);
        any.Start();
        return any;
    }

    private void AcceptLoop()
    {
        while (running)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                if (!running)
                {
                    return;
                }
                continue;
            }
            Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            string reply;
            try
            {
                using StreamReader reader = new(stream, new UTF8Encoding(false), false, 4096, true);
                string request = await reader.ReadLineAsync() ?? "";
                reply = await handleRequest(request.Trim());
            }
            catch (Exception ex)
            {
                reply = $"{ERROR_PREFIX}{ex.GetType().Name}: {ex.Message}";
            }
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(reply ?? "");
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
            }
            catch (Exception)
            {
                // Caller went away.
            }
        }
    }
}
#endif
