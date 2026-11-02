#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.DevBridge;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     Debug-only localhost endpoint that lets agents control and inspect this game from the shell
///     (<c>tools/bridge.ps1 &lt;player&gt; &lt;command&gt;</c>, see <see cref="BridgeCommands.HELP" />). Requests arrive on
///     a listener thread and run here, on the Unity main thread, in <see cref="Update" />.
/// </summary>
public sealed class DevBridge : MonoBehaviour
{
    private readonly ConcurrentQueue<Request> requests = new();
    private BridgeListener? listener;

    /// <summary>
    ///     The <c>--instantlaunch &lt;name&gt;</c> player name, or <c>pid&lt;process id&gt;</c> for games started otherwise.
    /// </summary>
    public static string GetBridgeName()
    {
        string[] args = NitroxEnvironment.CommandLineArgs;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--instantlaunch", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return $"pid{Process.GetCurrentProcess().Id}";
    }

    private void Start()
    {
        string bridgeName = GetBridgeName();
        try
        {
            listener = new BridgeListener(bridgeName, Enqueue);
            Log.Info($"Dev bridge for '{bridgeName}' listening on 127.0.0.1:{listener.Port} (tools\\bridge.ps1 {bridgeName} help)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start the dev bridge");
        }
    }

    private void Update()
    {
        while (requests.TryDequeue(out Request request))
        {
            string reply;
            try
            {
                object? result = BridgeCommands.Execute(request.Line, this);
                reply = result as string ?? BridgeReflection.ToJson(result);
            }
            catch (Exception ex)
            {
                Exception inner = ex is System.Reflection.TargetInvocationException { InnerException: { } invocationInner } ? invocationInner : ex;
                reply = $"{BridgeListener.ERROR_PREFIX}{inner.GetType().Name}: {inner.Message}";
                Log.Debug($"Dev bridge request '{request.Line}' failed: {inner}");
            }
            request.Reply.SetResult(reply);
        }
    }

    private void OnDestroy()
    {
        listener?.Dispose();
    }

    private Task<string> Enqueue(string line)
    {
        // Continuations (writing the reply to the socket) must not run on the main thread.
        TaskCompletionSource<string> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        requests.Enqueue(new Request(line, reply));
        return reply.Task;
    }

    private sealed class Request(string line, TaskCompletionSource<string> reply)
    {
        public string Line { get; } = line;
        public TaskCompletionSource<string> Reply { get; } = reply;
    }
}
#endif
