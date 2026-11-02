using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.GameLogic.Helper;

/// <summary>
///     Keeps the scanner room's resource database (<see cref="ResourceTrackerDatabase" />) free of resources another player
///     removed. <see cref="ResourceTracker" /> has no OnDestroy: it only unregisters through local callbacks
///     (OnPickedUp, OnBreakResource, ...), which never run for a remote removal, and entries of unloaded cells stay
///     registered. Left alone, scanner rooms keep pinging broken outcrops and picked up resources (#2046, #2151).
/// </summary>
public static class ResourceTrackerHelper
{
    /// <summary>
    ///     Removes the resource trackers of a GameObject that is about to be destroyed because of another player.
    /// </summary>
    public static void Forget(GameObject gameObject)
    {
        if (!gameObject)
        {
            return;
        }
        foreach (ResourceTracker resourceTracker in gameObject.GetComponentsInChildren<ResourceTracker>(true))
        {
            resourceTracker.Unregister();
        }
    }

    /// <summary>
    ///     Removes the resource with this id from every tech type of the database, for objects that aren't loaded.
    ///     Resource trackers use the object's UniqueIdentifier id, which Nitrox sets to the NitroxId.
    /// </summary>
    public static void Forget(NitroxId id)
    {
        string uniqueId = id.ToString();
        List<TechType> techTypes = ResourceTrackerDatabase.resources.Where(pair => pair.Value.ContainsKey(uniqueId)).Select(pair => pair.Key).ToList();
        foreach (TechType techType in techTypes)
        {
            ResourceTrackerDatabase.Unregister(uniqueId, techType);
        }
    }
}
