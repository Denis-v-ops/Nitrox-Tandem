#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     Per-frame recordings of an object's pose for the dev bridge (<c>record start|stop|list</c>), so movement
///     replication can be measured: both games record the same object and a script compares the tracks by wall clock
///     (both games run on one PC). Samples are taken in <see cref="LateUpdate" />, after every Update (movement
///     replicators, Cyclops pawns), so they are the pose that gets rendered.
/// </summary>
internal sealed class BridgeRecorder : MonoBehaviour
{
    private const int MAX_SAMPLES = 60000;

    private static readonly string[] fields =
    [
        "ms", "qpc", "frame", "dt", "rt",
        "px", "py", "pz", "ex", "ey", "ez",
        "lx", "ly", "lz",
        "vx", "vy", "vz", "wx", "wy", "wz",
        "maxLat", "buf",
        "hx", "hy", "hz", "grounded"
    ];

    private static readonly FieldInfo replicatorBufferField = typeof(MovementReplicator).GetField("buffer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo watchedEntriesField = typeof(MovementBroadcaster).GetField("watchedEntries", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Dictionary<string, Recording> recordings = new();

    public static object Command(List<string> args, MonoBehaviour host)
    {
        if (!host.gameObject.TryGetComponent(out BridgeRecorder recorder))
        {
            recorder = host.gameObject.AddComponent<BridgeRecorder>();
        }
        string usage = "record start <name> <target> [relativeTo] | record stop <name> | record list";
        if (args.Count == 0)
        {
            throw new ArgumentException(usage);
        }
        switch (args[0].ToLowerInvariant())
        {
            case "start":
                if (args.Count < 3)
                {
                    throw new ArgumentException(usage);
                }
                GameObject target = BridgeReflection.ResolveTarget(args[2]);
                GameObject? relativeTo = args.Count > 3 ? BridgeReflection.ResolveTarget(args[3]) : null;
                recorder.recordings[args[1]] = new Recording(args[1], target, relativeTo);
                return new Dictionary<string, object?> { ["started"] = args[1], ["target"] = BridgeReflection.Summary(target) };
            case "stop":
                if (args.Count < 2)
                {
                    throw new ArgumentException(usage);
                }
                if (!recorder.recordings.TryGetValue(args[1], out Recording recording))
                {
                    throw new InvalidOperationException($"No recording named {args[1]}");
                }
                recorder.recordings.Remove(args[1]);
                return recording.ToJson();
            case "list":
                List<object> list = [];
                foreach (Recording running in recorder.recordings.Values)
                {
                    list.Add(new Dictionary<string, object?> { ["name"] = running.Name, ["samples"] = running.Rows.Count });
                }
                return list;
            default:
                throw new ArgumentException(usage);
        }
    }

    private void LateUpdate()
    {
        foreach (Recording recording in recordings.Values)
        {
            recording.Sample();
        }
    }

    private sealed class Recording(string name, GameObject target, GameObject? relativeTo)
    {
        public string Name { get; } = name;
        public List<double?[]> Rows { get; } = [];
        private readonly List<string> events = [];
        private readonly Dictionary<string, string> lastState = new();
        private readonly NitroxId? targetId = target.TryGetNitroxId(out NitroxId id) ? id : null;
        private readonly bool isLocalPlayer = target == Player.mainObject;

        public void Sample()
        {
            if (Rows.Count >= MAX_SAMPLES)
            {
                return;
            }
            double ms = DateTime.UtcNow.Ticks / 10000.0;
            if (!target)
            {
                Event(ms, "target", "destroyed");
                return;
            }

            Transform transform = target.transform;
            Vector3 position = transform.position;
            Vector3 euler = transform.rotation.eulerAngles;
            Vector3? local = relativeTo ? relativeTo!.transform.InverseTransformPoint(position) : null;
            Rigidbody rigidbody = target.GetComponent<Rigidbody>();
            MovementReplicator replicator = target.GetComponent<MovementReplicator>();
            Vector3? handle = null;
            double? grounded = null;
            if (isLocalPlayer)
            {
                CyclopsMotor cyclopsMotor = target.GetComponent<CyclopsMotor>();
                if (cyclopsMotor && cyclopsMotor.Pawn != null && cyclopsMotor.Pawn.Handle)
                {
                    handle = cyclopsMotor.Pawn.Handle.transform.localPosition;
                }
                grounded = Player.main.groundMotor && Player.main.groundMotor.IsGrounded() ? 1 : 0;
            }

            int? bufferCount = null;
            if (replicator && replicatorBufferField?.GetValue(replicator) is { } buffer)
            {
                bufferCount = buffer.GetType().GetProperty("Count")?.GetValue(buffer) as int?;
            }

            Rows.Add(
            [
                Math.Round(ms, 2), Math.Round(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency, 3), Time.frameCount, Math.Round(Time.unscaledDeltaTime, 5),
                Math.Round(NitroxServiceLocator.LocateService<TimeManager>().RealTimeElapsed, 4),
                R(position.x), R(position.y), R(position.z), R(euler.x), R(euler.y), R(euler.z),
                R(local?.x), R(local?.y), R(local?.z),
                R(rigidbody ? rigidbody.velocity.x : null), R(rigidbody ? rigidbody.velocity.y : null), R(rigidbody ? rigidbody.velocity.z : null),
                R(rigidbody ? rigidbody.angularVelocity.x : null), R(rigidbody ? rigidbody.angularVelocity.y : null), R(rigidbody ? rigidbody.angularVelocity.z : null),
                replicator ? Math.Round(replicator.MaxAllowedLatency, 4) : null, bufferCount,
                R(handle?.x), R(handle?.y), R(handle?.z), grounded
            ]);

            // Discrete state, recorded only when it changes
            Track(ms, "parent", transform.parent ? transform.parent.name : "-");
            Track(ms, "replicator", replicator ? (replicator.enabled ? replicator.GetType().Name : "disabled") : "-");
            if (rigidbody)
            {
                Track(ms, "kinematic", rigidbody.isKinematic.ToString());
                Track(ms, "interpolation", rigidbody.interpolation.ToString());
            }
            if (target.TryGetComponent(out WorldForces worldForces))
            {
                Track(ms, "worldForces", worldForces.enabled.ToString());
            }
            if (target.TryGetComponent(out Stabilizer stabilizer))
            {
                Track(ms, "stabilizer", stabilizer.stabilizerEnabled.ToString());
            }
            if (targetId != null)
            {
                SimulationOwnership simulationOwnership = NitroxServiceLocator.LocateService<SimulationOwnership>();
                Track(ms, "lock", simulationOwnership.TryGetLockType(targetId, out SimulationLockType lockType) ? lockType.ToString() : "-");
                if (MovementBroadcaster.Instance && watchedEntriesField?.GetValue(MovementBroadcaster.Instance) is System.Collections.IDictionary watched)
                {
                    Track(ms, "broadcasting", watched.Contains(targetId).ToString());
                }
            }
            if (isLocalPlayer)
            {
                Player player = Player.main;
                Track(ms, "sub", player.currentSub ? $"{player.currentSub.name} {BridgeReflection.IdOf(player.currentSub.gameObject)}" : "-");
                Track(ms, "mode", player.mode.ToString());
                Track(ms, "motorMode", player.motorMode.ToString());
                Track(ms, "cinematic", player.cinematicModeActive.ToString());
            }
        }

        private void Track(double ms, string key, string value)
        {
            if (!lastState.TryGetValue(key, out string last) || last != value)
            {
                lastState[key] = value;
                Event(ms, key, value);
            }
        }

        private void Event(double ms, string key, string value)
        {
            events.Add($"[{ms.ToString("0.##", CultureInfo.InvariantCulture)},{Time.frameCount},\"{key}\",\"{value.Replace("\"", "'")}\"]");
        }

        private static double? R(float? value) => value.HasValue ? Math.Round(value.Value, 4) : null;

        public string ToJson()
        {
            StringBuilder builder = new();
            builder.Append("{\"name\":\"").Append(Name).Append("\",\"fields\":[");
            builder.Append(string.Join(",", Array.ConvertAll(fields, f => $"\"{f}\"")));
            builder.Append("],\"rows\":[");
            for (int i = 0; i < Rows.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append('[');
                double?[] row = Rows[i];
                for (int j = 0; j < row.Length; j++)
                {
                    if (j > 0)
                    {
                        builder.Append(',');
                    }
                    builder.Append(row[j].HasValue ? row[j]!.Value.ToString("R", CultureInfo.InvariantCulture) : "null");
                }
                builder.Append(']');
            }
            builder.Append("],\"events\":[").Append(string.Join(",", events)).Append("]}");
            return builder.ToString();
        }
    }
}
#endif
