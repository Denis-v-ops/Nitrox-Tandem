using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Helper;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

public sealed class EntityDestroyedProcessor(Entities entities) : IClientPacketProcessor<EntityDestroyed>
{
    public const DamageType DAMAGE_TYPE_RUN_ORIGINAL = (DamageType)100;

    private readonly Entities entities = entities;

    public Task Process(ClientProcessorContext context, EntityDestroyed packet)
    {
        entities.RemoveEntity(packet.Id);

        if (entities.SpawningEntities)
        {
            entities.MarkForDeletion(packet.Id);
        }

        if (!NitroxEntity.TryGetObjectFrom(packet.Id, out GameObject gameObject))
        {
            // The server sends destroys of entities in cells we don't have loaded too, so that scanner rooms forget them
            ResourceTrackerHelper.Forget(packet.Id);
            Log.Debug($"[{nameof(EntityDestroyedProcessor)}] Could not find entity with id: {packet.Id} to destroy.");
            return Task.CompletedTask;
        }

        using (PacketSuppressor<EntityDestroyed>.Suppress())
        {
            ResourceTrackerHelper.Forget(gameObject);
            // This type of check could get out of control if there are many types with custom destroy logic. If we get a couple more, move to separate processors.
            if (gameObject.TryGetComponent(out Vehicle vehicle))
            {
                DestroyVehicle(vehicle);
            }
            else if (gameObject.TryGetComponent(out SubRoot subRoot))
            {
                DestroySubroot(subRoot);
            }
            else if (gameObject.TryGetComponent(out Pickupable pickupable))
            {
                DestroyPickupable(pickupable);
            }
            else if (gameObject.TryGetComponent(out CyclopsDamagePoint cyclopsDamagePoint))
            {
                DestroyCyclopsDamagePoint(cyclopsDamagePoint);
            }
            else if (gameObject.TryGetComponent(out Warper warper))
            {
                DestroyWarper(warper);
            }
            else
            {
                Entities.DestroyObject(gameObject);
            }
        }
        return Task.CompletedTask;
    }

    private void DestroyVehicle(Vehicle vehicle)
    {
        if (vehicle.GetPilotingMode()) //Check Local Object Have Player inside
        {
            vehicle.OnPilotModeEnd();

            if (!Player.main.ToNormalMode())
            {
                Player.main.ToNormalMode(false);
                Player.main.transform.parent = null;
            }
        }

        foreach (RemotePlayerIdentifier identifier in vehicle.GetComponentsInChildren<RemotePlayerIdentifier>(true))
        {
            identifier.RemotePlayer.ResetStates();
        }

        if (vehicle.gameObject)
        {
            if (vehicle.destructionEffect)
            {
                GameObject gameObject = Object.Instantiate(vehicle.destructionEffect);
                gameObject.transform.position = vehicle.transform.position;
                gameObject.transform.rotation = vehicle.transform.rotation;
            }

            Object.Destroy(vehicle.gameObject);
        }
    }

    private void DestroySubroot(SubRoot subRoot)
    {
        DamageInfo damageInfo = new() { type = DAMAGE_TYPE_RUN_ORIGINAL };
        if (subRoot.live.health > 0f)
        {
            // oldHPPercent must be in the interval [0; 0.25[ because else, SubRoot.OnTakeDamage will end up in the wrong else condition
            subRoot.oldHPPercent = 0f;
            subRoot.live.health = 0f;
            subRoot.live.NotifyAllAttachedDamageReceivers(damageInfo);
            subRoot.live.Kill();
        }

        // We use a specific DamageType so that the Prefix on this method will accept this call
        subRoot.OnTakeDamage(damageInfo);
    }

    private void DestroyPickupable(Pickupable pickupable)
    {
        // The OnDestroy method on Pickupable can send extra EntityDestroyed packets if the item is in an Equipment container, causing a loop.
        // The packet suppressor does not help since destroying an object is not synchronous and only happens at the end of a frame.
        // Calling OnDestroy now means the offending SetInventoryItem call only runs once and additional packets are suppressed.

        pickupable.OnDestroy();
        Object.Destroy(pickupable.gameObject);
    }

    private void DestroyCyclopsDamagePoint(CyclopsDamagePoint cyclopsDamagePoint)
    {
        cyclopsDamagePoint.liveMixin.AddHealth(999);
    }

    /// <summary>
    ///     COOP_FEATURES_SPEC B1: a living warper started to warp out in the game that simulates it (Warper_WarpOut_Patch). It warps
    ///     out here too: the same animation and effect, and its spawner (if it is ours, see WarperEntitySpawner) restarts its
    ///     timer as it does in that game. A dead one (its corpse removed) is just destroyed.
    /// </summary>
    private static void DestroyWarper(Warper warper)
    {
        if (warper.isActiveAndEnabled && warper.liveMixin && warper.liveMixin.IsAlive())
        {
            // Destroys it after the animation (Invoke of EndWarpOut); we don't simulate it, so that sends nothing
            warper.WarpOut();
            return;
        }
        WarperSpawner spawner = warper.spawner.AliveOrNull();
        if (spawner)
        {
            spawner.OnWarpOut();
        }
        Entities.DestroyObject(warper.gameObject);
    }
}
