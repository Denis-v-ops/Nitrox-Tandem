using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using UnityEngine;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     Keeps the charge of base power sources (solar panels, thermal plants, bioreactors, nuclear reactors) the same in
///     every game and on the server (COOP_FEATURES_SPEC B3, #1811). Before, the server never heard of it and every spawn
///     filled the source to its maximum.
/// </summary>
/// <remarks>
///     <para>
///         Every game keeps simulating its own copy (generation in <c>SolarPanel.Update</c>, <c>ThermalPlant.AddPower</c>,
///         the reactors' <c>Update</c>, and consumption through <c>PowerRelay</c>): the game holding the lock (see
///         <see cref="IsLocalProducer" />) sends its value now and then, and the other games overwrite theirs with it.
///         Nothing is stopped in the other games, so their base never goes dark between two updates (a base whose relay
///         has no power has no air either, <c>Player.CanBreathe</c>). What the other games consume themselves (e.g. the
///         partner's fabricator) isn't taken from the shared value.
///     </para>
///     <para>
///         Throttle: <c>PowerSource</c> calls <c>UpdateConnectionCallback</c> once a second (game 83031), which calls
///         <see cref="Tick" />. A value is sent when it changed by at least <see cref="MIN_CHANGE_RATIO" /> of the source's
///         maximum and the last one is <see cref="MIN_INTERVAL_SECONDS" /> old, or at once when the source became empty,
///         non-empty or full (that's what turns a base's power on or off). The fastest change in a base is a nuclear
///         reactor's 4.2 energy/s (2500 max), so a saved value is at most about 6 s, or 1 % of the maximum, behind; an
///         idle full base sends nothing.
///     </para>
/// </remarks>
public class BasePowerSync
{
    public const float MIN_INTERVAL_SECONDS = 5f;
    public const float MIN_CHANGE_RATIO = 0.01f;

    private readonly SimulationOwnership simulationOwnership;
    private readonly Entities entities;
    private readonly Dictionary<NitroxId, Sent> lastSentById = [];

    public BasePowerSync(SimulationOwnership simulationOwnership, Entities entities)
    {
        this.simulationOwnership = simulationOwnership;
        this.entities = entities;
    }

    /// <summary>
    ///     Power to give a spawned source: the stored value, or the maximum when nothing is stored (saves from before B3,
    ///     a module that was just built, a thermal plant whose metadata only has its temperature).
    /// </summary>
    public static float RestoredPower(EntityMetadata metadata, float maxPower)
    {
        float? stored = metadata switch
        {
            PowerSourceMetadata powerSourceMetadata => powerSourceMetadata.Power,
            ThermalPlantMetadata thermalPlantMetadata => thermalPlantMetadata.Power,
            _ => null
        };
        return stored.HasValue ? Math.Min(Math.Max(stored.Value, 0f), maxPower) : maxPower;
    }

    /// <summary>
    ///     Whether a <see cref="PowerSource" /> is a synced base power source: the module itself (with a NitroxId) of a
    ///     solar panel, thermal plant, bioreactor or nuclear reactor. Not ghost previews, power cells or vehicles.
    /// </summary>
    public static bool IsSynced(PowerSource powerSource)
    {
        GameObject gameObject = powerSource.gameObject;
        return gameObject.TryGetComponent(out SolarPanel _) || gameObject.TryGetComponent(out ThermalPlant _) ||
               gameObject.TryGetComponent(out BaseBioReactor _) || gameObject.TryGetComponent(out BaseNuclearReactor _);
    }

    /// <summary>
    ///     The game holding the simulation lock on the source's base (like <see cref="BaseModuleSimulation" />), or on the
    ///     source itself when it isn't part of a base (a solar panel or thermal plant standing alone, a global root entity
    ///     of its own). The server only accepts power from the same player (<c>EntityMetadataUpdateProcessor</c>).
    /// </summary>
    public bool IsLocalProducer(PowerSource powerSource, NitroxId powerSourceId)
    {
        SubRoot subRoot = powerSource.GetComponentInParent<SubRoot>();
        NitroxId lockId = subRoot && subRoot.TryGetNitroxId(out NitroxId baseId) ? baseId : powerSourceId;
        return simulationOwnership.HasAnyLockType(lockId);
    }

    public void Tick(PowerSource powerSource)
    {
        if (!IsSynced(powerSource) || !powerSource.TryGetNitroxId(out NitroxId id))
        {
            return;
        }
        if (!IsLocalProducer(powerSource, id))
        {
            // When this game gets the lock later, its first tick sends its value
            lastSentById.Remove(id);
            return;
        }

        float power = powerSource.power;
        float maxPower = powerSource.maxPower;
        float now = Time.unscaledTime;
        if (lastSentById.TryGetValue(id, out Sent last) && !ShouldSend(last.Power, power, maxPower, now - last.Time))
        {
            return;
        }

        lastSentById[id] = new Sent(power, now);
        entities.BroadcastMetadataUpdate(id, CreateMetadata(powerSource, power));
    }

    public static bool ShouldSend(float lastPower, float power, float maxPower, float secondsSinceLast)
    {
        if (IsEmpty(lastPower) != IsEmpty(power) || IsFull(lastPower, maxPower) != IsFull(power, maxPower))
        {
            return true;
        }
        return secondsSinceLast >= MIN_INTERVAL_SECONDS && Math.Abs(power - lastPower) >= Math.Max(MIN_CHANGE_RATIO * maxPower, 0.01f);
    }

    public static EntityMetadata CreateMetadata(PowerSource powerSource, float power)
    {
        // A thermal plant has one metadata already: its power goes with its temperature
        if (powerSource.TryGetComponent(out ThermalPlant thermalPlant))
        {
            return new ThermalPlantMetadata(thermalPlant.temperature, power);
        }
        return new PowerSourceMetadata(power);
    }

    private static bool IsEmpty(float power) => power <= 0f;

    private static bool IsFull(float power, float maxPower) => power >= maxPower;

    private readonly record struct Sent(float Power, float Time);
}
