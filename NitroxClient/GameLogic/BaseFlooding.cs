using System;
using System.Runtime.CompilerServices;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Bases;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
///     Syncs the water level of flooding bases (<see cref="BaseFloodSim" />) through <see cref="BaseFloodMetadata" /> on
///     the base entity. Every player still simulates the flooding locally from the synced leaks. The player holding the
///     base's simulation lock sends its levels at most every <see cref="SEND_INTERVAL" /> seconds when they changed, and
///     the others overwrite theirs with them. The server stores the latest levels, so a player who joins or reconnects
///     starts from them instead of from a dry base.
/// </summary>
public class BaseFlooding
{
    public const float SEND_INTERVAL = 2f;

    private readonly SimulationOwnership simulationOwnership;
    // Not Entities: Entities -> EntityMetadataManager -> BaseFloodMetadataProcessor -> BaseFlooding would be a cycle
    private readonly IPacketSender packetSender;
    private readonly ConditionalWeakTable<BaseFloodSim, SyncState> states = new();

    public BaseFlooding(SimulationOwnership simulationOwnership, IPacketSender packetSender)
    {
        this.simulationOwnership = simulationOwnership;
        this.packetSender = packetSender;
    }

    /// <summary>
    ///     Called after every <see cref="BaseFloodSim" />.Update. Only compares a time stamp unless a send is due.
    /// </summary>
    public void AfterUpdate(BaseFloodSim floodSim)
    {
        SyncState state = states.GetOrCreateValue(floodSim);
        if (state.Pending != null)
        {
            TryApplyPending(floodSim, state);
        }

        if (Time.unscaledTime < state.NextSendCheck)
        {
            return;
        }
        state.NextSendCheck = Time.unscaledTime + SEND_INTERVAL;

        // Levels received from the server must be in place before this player's own simulation is worth sending.
        if (state.Pending != null || !floodSim.IsInitialized() || (BuildingHandler.Main && BuildingHandler.Main.Resyncing))
        {
            return;
        }
        if (!floodSim.baseComp || !floodSim.baseComp.TryGetNitroxId(out NitroxId baseId) || !simulationOwnership.HasAnyLockType(baseId))
        {
            return;
        }

        float[] levels = BaseFloodLevels.Snapshot(floodSim.cellWaterLevel);
        if (!BaseFloodLevels.HasChanged(state.LastSent, levels))
        {
            return;
        }
        state.LastSent = levels;
        state.Seeded = true;
        packetSender.Send(new EntityMetadataUpdate(baseId, new BaseFloodMetadata(ToGridSize(floodSim.shape), levels)));
    }

    /// <summary>
    ///     Applies levels from the server: when the base spawns (always) and later updates from the lock holder (ignored by
    ///     the lock holder itself, whose simulation is the reference).
    /// </summary>
    public void ApplyMetadata(BaseFloodSim floodSim, BaseFloodMetadata metadata)
    {
        SyncState state = states.GetOrCreateValue(floodSim);
        if (state.Seeded && floodSim.baseComp && floodSim.baseComp.TryGetNitroxId(out NitroxId baseId) && simulationOwnership.HasAnyLockType(baseId))
        {
            return;
        }
        state.Pending = metadata;
        TryApplyPending(floodSim, state);
    }

    private static void TryApplyPending(BaseFloodSim floodSim, SyncState state)
    {
        // The flood grid only exists once the base geometry was built (BaseFloodSim.OnPostRebuildGeometry)
        if (!floodSim.IsInitialized() || floodSim.cellWaterLevel == null)
        {
            return;
        }

        BaseFloodMetadata metadata = state.Pending;
        state.Pending = null;
        state.Seeded = true;

        if (metadata.IsDry)
        {
            Array.Clear(floodSim.cellWaterLevel, 0, floodSim.cellWaterLevel.Length);
            state.LastSent = [];
            return;
        }
        if (!metadata.GridSize.Equals(ToGridSize(floodSim.shape)) || metadata.CellWaterLevels.Length != floodSim.cellWaterLevel.Length)
        {
            // The base was rebuilt since these levels were taken: they don't map to the same cells anymore.
            Log.Debug($"[{nameof(BaseFlooding)}] Ignoring flood levels for grid {metadata.GridSize}, the base's grid is {ToGridSize(floodSim.shape)}");
            return;
        }
        Array.Copy(metadata.CellWaterLevels, floodSim.cellWaterLevel, metadata.CellWaterLevels.Length);
        // So that the lock holder doesn't send back what it just received
        state.LastSent = (float[])metadata.CellWaterLevels.Clone();
    }

    private static NitroxInt3 ToGridSize(Grid3Shape shape) => new(shape.x, shape.y, shape.z);

    private sealed class SyncState
    {
        public float NextSendCheck;
        public float[] LastSent;
        public BaseFloodMetadata Pending;
        public bool Seeded;
    }
}

/// <summary>
///     The engine-independent part of <see cref="BaseFlooding" />.
/// </summary>
public static class BaseFloodLevels
{
    /// <summary>
    ///     Levels below this count as dry; changes below it aren't worth a packet.
    /// </summary>
    public const float EPSILON = 0.01f;

    /// <summary>
    ///     A copy of the levels, or an empty array when every cell is (nearly) dry.
    /// </summary>
    public static float[] Snapshot(float[] cellWaterLevel)
    {
        if (cellWaterLevel == null)
        {
            return [];
        }
        foreach (float level in cellWaterLevel)
        {
            if (level >= EPSILON)
            {
                return (float[])cellWaterLevel.Clone();
            }
        }
        return [];
    }

    public static bool HasChanged(float[] lastSent, float[] current)
    {
        if (lastSent == null)
        {
            return true;
        }
        if (lastSent.Length != current.Length)
        {
            return true;
        }
        for (int i = 0; i < current.Length; i++)
        {
            if (Math.Abs(current[i] - lastSent[i]) >= EPSILON)
            {
                return true;
            }
        }
        return false;
    }
}
