#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Debuggers.Bridge;

/// <summary>
///     Ring buffer of recently started FMOD events (<c>sounds [seconds] [filter]</c>) and a read of the PDA voice queue
///     (<c>pdaqueue</c>). Fed by Debug-only hooks where the game and Nitrox start sounds: the emitters
///     (<c>FMOD_CustomEmitter.OnPlay</c>, <c>FMOD_StudioEventEmitter.StartEvent/PlayUI</c>, FMODUnity's
///     <c>StudioEventEmitter.Play</c>), one-shots (<c>FMODUWE.PlayOneShotImpl</c>), the PDA voice
///     (<c>SoundQueue.Play</c>) and the raw event instances <see cref="FMODEmitterController" /> starts for remote
///     players. Windowless games mute the master channel group only, so events still start and get recorded.
/// </summary>
public static class BridgeSounds
{
    private const int CAPACITY = 512;
    private static readonly Queue<Entry> entries = new();
    private static readonly object entriesLock = new();

    public static void Record(string source, string? path, Component? emitter)
    {
        try
        {
            Record(source, path, emitter ? emitter!.transform.position : null, emitter ? emitter!.gameObject : null);
        }
        catch (Exception ex)
        {
            Log.Debug($"[{nameof(BridgeSounds)}] {source} {path}: {ex.Message}");
        }
    }

    public static void Record(string source, string? path, Vector3? position, GameObject? owner = null)
    {
        try
        {
            Entry entry = new()
            {
                Time = Time.time,
                Frame = Time.frameCount,
                Source = source,
                Path = path ?? "",
                Position = position
            };
            if (owner)
            {
                entry.Object = owner!.name;
                if (owner.TryGetComponentInParent(out NitroxEntity entity, true) && entity.Id != null)
                {
                    entry.OwnerId = entity.Id.ToString();
                    entry.OwnerName = entity.gameObject.name;
                }
                if (owner.TryGetComponentInParent(out RemotePlayerIdentifier remote, true))
                {
                    entry.RemotePlayer = remote.RemotePlayer?.PlayerName;
                }
                entry.LocalPlayer = Player.main && owner.transform.IsChildOf(Player.main.transform);
            }
            if (position.HasValue && Player.main)
            {
                entry.Distance = Vector3.Distance(position.Value, Player.main.transform.position);
            }
            lock (entriesLock)
            {
                entries.Enqueue(entry);
                while (entries.Count > CAPACITY)
                {
                    entries.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[{nameof(BridgeSounds)}] {source} {path}: {ex.Message}");
        }
    }

    /// <summary>
    ///     <c>sounds [seconds] [filter]</c>: the events started in the last <c>seconds</c> (default all kept), whose path,
    ///     source, object or player contains <c>filter</c>. <c>sounds clear</c> empties the buffer.
    /// </summary>
    public static object Command(List<string> args)
    {
        if (args.Count > 0 && args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            lock (entriesLock)
            {
                entries.Clear();
            }
            return "cleared";
        }
        float since = args.Count > 0 ? float.Parse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture) : float.MaxValue;
        string? filter = args.Count > 1 ? args[1] : null;
        float now = Time.time;
        List<Entry> snapshot;
        lock (entriesLock)
        {
            snapshot = entries.ToList();
        }
        return snapshot.Where(e => now - e.Time <= since)
                       .Where(e => filter == null || Matches(e, filter))
                       .Select(e => new Dictionary<string, object?>
                       {
                           ["ago"] = Math.Round(now - e.Time, 2),
                           ["frame"] = e.Frame,
                           ["source"] = e.Source,
                           ["path"] = e.Path,
                           ["position"] = e.Position.HasValue ? BridgeReflection.Round(e.Position.Value) : null,
                           ["distance"] = e.Distance.HasValue ? Math.Round(e.Distance.Value, 1) : null,
                           ["object"] = e.Object,
                           ["ownerId"] = e.OwnerId,
                           ["ownerName"] = e.OwnerName,
                           ["remotePlayer"] = e.RemotePlayer,
                           ["localPlayer"] = e.LocalPlayer
                       })
                       .ToList();
    }

    private static bool Matches(Entry entry, string filter) =>
        Contains(entry.Path, filter) || Contains(entry.Source, filter) || Contains(entry.Object, filter) || Contains(entry.OwnerName, filter) ||
        Contains(entry.RemotePlayer, filter) || Contains(entry.OwnerId, filter);

    private static bool Contains(string? text, string filter) => text != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    ///     <c>pdaqueue</c>: the PDA voice line playing now and the ones waiting (<c>PDASounds.queue</c>). Lines that
    ///     already played are in <c>sounds 600 SoundQueue</c>.
    /// </summary>
    public static object PdaQueue()
    {
        SoundQueue queue = PDASounds.queue;
        if (queue == null)
        {
            return new Dictionary<string, object?> { ["initialized"] = false };
        }
        return new Dictionary<string, object?>
        {
            ["initialized"] = true,
            ["current"] = queue.current,
            ["positionSeconds"] = Math.Round(queue.positionSeconds, 2),
            ["lengthSeconds"] = Math.Round(queue.lengthSeconds, 2),
            ["queued"] = queue.queue.Select(entry => new Dictionary<string, object?> { ["sound"] = entry.sound, ["subtitles"] = entry.subtitles }).ToList()
        };
    }

    private sealed class Entry
    {
        public float Time;
        public int Frame;
        public string Source = "";
        public string Path = "";
        public Vector3? Position;
        public float? Distance;
        public string? Object;
        public string? OwnerId;
        public string? OwnerName;
        public string? RemotePlayer;
        public bool LocalPlayer;
    }
}
#endif
