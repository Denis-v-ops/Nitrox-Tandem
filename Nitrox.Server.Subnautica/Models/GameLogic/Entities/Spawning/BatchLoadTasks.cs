using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

/// <summary>
///     The load task per batch that <see cref="BatchEntitySpawner" /> (generation) and <see cref="WorldEntityManager" />
///     (registration) keep, and the record of generated batches the save keeps (<c>WorldData.ParsedBatchCells</c>).
/// </summary>
/// <remarks>
///     A batch in the record is never generated again: its entities are expected in the save's entity data. So a batch
///     that is still generating, or whose generation failed, must not be in it, or its area stays empty for good after
///     a restart.
/// </remarks>
internal static class BatchLoadTasks
{
    /// <summary>
    ///     The batch's task: the running or finished one, or a new one from <paramref name="start" />. A task that fails is
    ///     forgotten, so the next request for the batch tries again.
    /// </summary>
    internal static Task<T> GetOrStart<T>(ConcurrentDictionary<NitroxInt3, Lazy<Task<T>>> tasks, NitroxInt3 batchId, Func<NitroxInt3, Task<T>> start)
    {
        return tasks.GetOrAdd(batchId, NewEntry).Value;

        Lazy<Task<T>> NewEntry(NitroxInt3 id)
        {
            Lazy<Task<T>>? entry = null;
            entry = new Lazy<Task<T>>(() =>
            {
                Task<T> task;
                try
                {
                    task = start(id);
                }
                catch (Exception ex)
                {
                    task = Task.FromException<T>(ex);
                }
                // Only this entry: a retry may have added a new one meanwhile
                task.ContinueWith(_ => tasks.TryRemove(new KeyValuePair<NitroxInt3, Lazy<Task<T>>>(id, entry!)), CancellationToken.None,
                                  TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return task;
            });
            return entry;
        }
    }

    /// <returns>The batches whose task finished successfully (restored ones included), except those that came out empty.</returns>
    internal static List<NitroxInt3> ListGenerated<T>(IEnumerable<KeyValuePair<NitroxInt3, Lazy<Task<T>>>> tasks, IEnumerable<NitroxInt3> emptyBatches)
    {
        HashSet<NitroxInt3> empty = [.. emptyBatches];
        return [.. tasks.Where(pair => IsDone(pair.Value) && !empty.Contains(pair.Key)).Select(pair => pair.Key)];
    }

    /// <returns>
    ///     <paramref name="batches" /> without those that have a task in <paramref name="tasks" /> that hasn't finished
    ///     successfully. A batch without a task there (e.g. restored from the save) stays.
    /// </returns>
    internal static List<NitroxInt3> ExceptUnfinished<T>(IEnumerable<NitroxInt3> batches, ConcurrentDictionary<NitroxInt3, Lazy<Task<T>>> tasks)
    {
        return [.. batches.Where(batchId => !tasks.TryGetValue(batchId, out Lazy<Task<T>>? task) || IsDone(task))];
    }

    /// <summary>
    ///     Replaces the record by the batches of a save. Each gets a task that is already done (with no entities: they are in
    ///     the entity data), so <see cref="ListGenerated{T}" /> keeps listing it although nobody asks for it.
    /// </summary>
    internal static void Restore(ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks, IEnumerable<NitroxInt3> batches)
    {
        tasks.Clear();
        foreach (NitroxInt3 batchId in batches)
        {
            // The value constructor: IsValueCreated is true at once, unlike with a factory nobody has run
            tasks.TryAdd(batchId, new Lazy<Task<List<Entity>>>(Task.FromResult(new List<Entity>())));
        }
    }

    // IsValueCreated first: reading Value would start the task of an entry that is just being added
    private static bool IsDone<T>(Lazy<Task<T>> task) => task.IsValueCreated && task.Value.IsCompletedSuccessfully;
}
