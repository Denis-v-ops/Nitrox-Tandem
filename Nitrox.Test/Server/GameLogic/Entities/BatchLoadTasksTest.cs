using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.GameLogic.Entities;

/// <summary>
///     MULTINAUGHTICA_SPEC M3: a batch still generating, or whose generation failed, is not recorded as generated (its area
///     would stay empty after a restart), and a failed batch is generated again on the next visit.
/// </summary>
[TestClass]
public class BatchLoadTasksTest
{
    private static readonly NitroxInt3 batchA = new(10, 18, 10);
    private static readonly NitroxInt3 batchB = new(11, 18, 10);
    private static readonly NitroxInt3 batchC = new(12, 18, 10);

    [TestMethod]
    public void ListGenerated_OnlyBatchesThatFinishedSuccessfully()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = new();
        TaskCompletionSource<List<Entity>> stillGenerating = new();
        BatchLoadTasks.GetOrStart(tasks, batchA, _ => Task.FromResult(new List<Entity>()));
        BatchLoadTasks.GetOrStart(tasks, batchB, _ => stillGenerating.Task);
        tasks[batchC] = new Lazy<Task<List<Entity>>>(Task.FromException<List<Entity>>(new IOException("batch file unreadable")));

        BatchLoadTasks.ListGenerated(tasks, []).Should().Equal(batchA);

        stillGenerating.SetResult([]);
        BatchLoadTasks.ListGenerated(tasks, []).Should().BeEquivalentTo([batchA, batchB]);
    }

    [TestMethod]
    public void ListGenerated_LeavesOutEmptyBatches()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = new();
        BatchLoadTasks.GetOrStart(tasks, batchA, _ => Task.FromResult(new List<Entity>()));
        BatchLoadTasks.GetOrStart(tasks, batchB, _ => Task.FromResult(new List<Entity>()));

        BatchLoadTasks.ListGenerated(tasks, [batchB]).Should().Equal(batchA);
    }

    [TestMethod]
    public void Restore_BatchesFromTheSaveStayListed()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<List<Entity>>>> tasks = new();
        BatchLoadTasks.GetOrStart(tasks, batchC, _ => Task.FromResult(new List<Entity>()));

        BatchLoadTasks.Restore(tasks, [batchA, batchB]);

        BatchLoadTasks.ListGenerated(tasks, []).Should().BeEquivalentTo([batchA, batchB], "nobody asked for them since the start, they are generated all the same");
    }

    [TestMethod]
    public async Task GetOrStart_AFailedLoadIsTriedAgain()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<int>>> tasks = new();
        int starts = 0;
        TaskCompletionSource<int> firstTry = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> first = BatchLoadTasks.GetOrStart(tasks, batchA, _ => ++starts == 1 ? firstTry.Task : Task.FromResult(42));
        BatchLoadTasks.GetOrStart(tasks, batchA, _ => ++starts == 1 ? firstTry.Task : Task.FromResult(42)).Should().BeSameAs(first, "a running load is shared");
        firstTry.SetException(new IOException("batch file unreadable"));
        await Invoking(() => first).Should().ThrowAsync<IOException>();
        await ServerHarness.WaitUntilAsync(() => !tasks.ContainsKey(batchA), "the failed load is forgotten");

        (await BatchLoadTasks.GetOrStart(tasks, batchA, _ => ++starts == 1 ? firstTry.Task : Task.FromResult(42))).Should().Be(42);
        starts.Should().Be(2);
    }

    [TestMethod]
    public async Task GetOrStart_ALoadThatFailsAtOnceIsTriedAgain()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<int>>> tasks = new();

        await Invoking(() => BatchLoadTasks.GetOrStart<int>(tasks, batchA, _ => throw new IOException("batch file unreadable"))).Should().ThrowAsync<IOException>();

        (await BatchLoadTasks.GetOrStart(tasks, batchA, _ => Task.FromResult(7))).Should().Be(7);
    }

    [TestMethod]
    public async Task GetOrStart_ASuccessfulLoadIsKept()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<int>>> tasks = new();
        int starts = 0;

        await BatchLoadTasks.GetOrStart(tasks, batchA, _ => Task.FromResult(++starts));
        await BatchLoadTasks.GetOrStart(tasks, batchA, _ => Task.FromResult(++starts));

        starts.Should().Be(1);
    }

    [TestMethod]
    public void ExceptUnfinished_LeavesOutBatchesWhoseEntitiesAreNotRegisteredYet()
    {
        ConcurrentDictionary<NitroxInt3, Lazy<Task<int>>> registrations = new();
        TaskCompletionSource<int> registering = new();
        BatchLoadTasks.GetOrStart(registrations, batchA, _ => Task.FromResult(3));
        BatchLoadTasks.GetOrStart(registrations, batchB, _ => registering.Task);

        // batchC: restored from the save, never registered in this run
        BatchLoadTasks.ExceptUnfinished([batchA, batchB, batchC], registrations).Should().BeEquivalentTo([batchA, batchC]);
    }

    /// <summary>
    ///     L2: a batch generated before a restart is still recorded after it, also by the first save after the start
    ///     (nobody has visited it since).
    /// </summary>
    [TestMethod]
    [TestCategory("L2")]
    public async Task GeneratedBatch_StaysRecordedAcrossRestarts()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-BatchRecord");
        // A batch near the start (any batch with entities would do)
        NitroxInt3 batch = new(13, 18, 13);
        int spawned = await server.WorldEntityManager.LoadUnspawnedEntitiesAsync(batch, true);
        spawned.Should().BePositive();
        server.WorldEntityManager.GetGeneratedBatches().Should().Contain(batch);

        await server.RestartAsync();
        await server.SaveAsync();

        server.WorldEntityManager.GetGeneratedBatches().Should().Contain(batch);
        using JsonDocument worldData = JsonDocument.Parse(File.ReadAllText(Path.Combine(server.SavePath, "WorldData.json")));
        worldData.RootElement.GetProperty("ParsedBatchCells").EnumerateArray()
                 .Select(cell => new NitroxInt3(cell.GetProperty("X").GetInt32(), cell.GetProperty("Y").GetInt32(), cell.GetProperty("Z").GetInt32()))
                 .Should().Contain(batch, "the second save still records it");
    }
}
