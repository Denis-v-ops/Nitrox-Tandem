using FluentAssertions.Equivalency;
using Microsoft.Extensions.Logging;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Players;
using Nitrox.Server.Subnautica.Models.GameLogic.Unlockables;
using Nitrox.Server.Subnautica.Models.Serialization;
using Nitrox.Server.Subnautica.Models.Serialization.SaveDataUpgrades;
using Nitrox.Server.Subnautica.Models.Serialization.World;

namespace Nitrox.Test.Helper;

/// <summary>
///     Saves entities with the server's own save code (<see cref="WorldService" />, JSON) and loads them back the way a
///     server start does. Use it to check that state survives a server restart.
/// </summary>
internal static class SaveLoadRoundTrip
{
    /// <summary>
    ///     Saves and loads the entity trees, then asserts that every entity in them came back with the same type and
    ///     deeply equal data, and that nothing was added.
    /// </summary>
    /// <returns>The registry as the server has it after loading.</returns>
    public static EntityRegistry AssertSurvivesSaveAndLoad(params Entity[] roots)
    {
        List<Entity> expectedEntities = roots.SelectMany(Flatten).ToList();
        EntityRegistry loaded = SaveAndLoad(roots);

        foreach (Entity expected in expectedEntities)
        {
            loaded.TryGetEntityById(expected.Id, out Entity actual).Should().BeTrue($"{expected.GetType().Name} {expected.Id} should exist after save and load");
            AssertSameEntity(actual, expected);
        }
        loaded.GetAllEntities().Select(entity => entity.Id).Should().BeEquivalentTo(expectedEntities.Select(entity => entity.Id), "save and load should not add entities");
        return loaded;
    }

    /// <summary>
    ///     Asserts both entities have the same type and the same saved data, including all children (in any order).
    /// </summary>
    public static void AssertSameEntity(Entity actual, Entity expected)
    {
        actual.Should().BeOfType(expected.GetType());
        actual.Should().BeEquivalentTo(expected, EntityEquivalency, $"{expected.GetType().Name} {expected.Id} should be unchanged after save and load");
    }

    /// <summary>
    ///     Registers the entity trees like a running server, saves them like <see cref="WorldService.SaveAsync" /> and
    ///     loads them like a server start.
    /// </summary>
    /// <returns>The registry as the server has it after loading.</returns>
    public static EntityRegistry SaveAndLoad(params Entity[] roots)
    {
        EntityRegistry registry = new(new ThrowOnErrorLogger<EntityRegistry>());
        foreach (Entity root in roots)
        {
            registry.AddOrUpdate(root);
        }
        // Only the members used by GetPersistentGlobalRootEntities are needed.
        WorldEntityManager worldEntityManager = new(null!, registry, null!, null!, null!, new ThrowOnErrorLogger<WorldEntityManager>())
        {
            globalRootEntitiesById = registry.GetEntities<GlobalRootEntity>().ToDictionary(entity => entity.Id)
        };

        PersistedWorldData loadedWorld = SaveAndLoad(new PersistedWorldData
        {
            WorldData = new WorldData
            {
                ParsedBatchCells = [],
                GameData = new GameData
                {
                    PDAState = new PdaStateData(),
                    StoryGoals = new StoryGoalData(),
                    StoryTiming = new StoryTimingData()
                }
            },
            PlayerData = PlayerData.From([]),
            GlobalRootData = GlobalRootData.From(worldEntityManager.GetPersistentGlobalRootEntities()),
            EntityData = EntityData.From(registry.GetAllEntities(true))
        });

        // Same as WorldService.LoadPersistedWorldIntoServicesAsync.
        EntityRegistry loaded = new(new ThrowOnErrorLogger<EntityRegistry>());
        loaded.AddEntities(loadedWorld.EntityData.Entities);
        loaded.AddEntitiesIgnoringDuplicate(loadedWorld.GlobalRootData.Entities.OfType<Entity>().ToList());
        return loaded;
    }

    /// <summary>
    ///     Writes the world to a temporary save folder with <see cref="WorldService.Save" /> and reads it back with
    ///     <see cref="WorldService.LoadPersistedWorld" />.
    /// </summary>
    public static PersistedWorldData SaveAndLoad(PersistedWorldData world)
    {
        string saveDir = Path.Combine(Path.GetTempPath(), "NitroxTest", $"{nameof(SaveLoadRoundTrip)}_{Guid.NewGuid():N}");
        try
        {
            // Save and LoadPersistedWorld only use the serializer and the logger.
            WorldService worldService = new(new ServerJsonSerializer(new ThrowOnErrorLogger<ServerJsonSerializer>()), null!, Array.Empty<SaveDataUpgrade>(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
                                            new ThrowOnErrorLogger<WorldService>());
            worldService.Save(world, saveDir).Should().BeTrue("the world should save");
            PersistedWorldData loaded = worldService.LoadPersistedWorld(saveDir);
            loaded.Should().NotBeNull("the saved world should load");
            return loaded;
        }
        finally
        {
            if (Directory.Exists(saveDir))
            {
                Directory.Delete(saveDir, true);
            }
        }
    }

    private static IEnumerable<Entity> Flatten(Entity entity) => entity.ChildEntities.SelectMany(Flatten).Prepend(entity);

    private static EquivalencyAssertionOptions<Entity> EntityEquivalency(EquivalencyAssertionOptions<Entity> options) =>
        options.RespectingRuntimeTypes()
               .ComparingByValue<NitroxId>()
               // Not saved: the parent link is rebuilt on load, the rest is computed from it or is runtime-only.
               .Excluding(member => member.DeclaringType == typeof(NitroxTransform) &&
                                    (member.Name == nameof(NitroxTransform.Parent) || member.Name == nameof(NitroxTransform.Position) || member.Name == nameof(NitroxTransform.Rotation) ||
                                     member.Name == nameof(NitroxTransform.LocalToWorldMatrix)))
               .Excluding(member => member.Name == nameof(WorldEntity.AbsoluteEntityCell))
               .Excluding(member => member.DeclaringType == typeof(InteriorPieceEntity) && member.Name == nameof(InteriorPieceEntity.IsWaterPark))
               .Excluding(member => member.DeclaringType == typeof(BuildEntity) && member.Name == nameof(BuildEntity.OperationId));

    /// <summary>
    ///     The save code catches exceptions and only logs them, so a test would just see "save failed". Throwing on error
    ///     logs makes the real cause the test failure.
    /// </summary>
    private sealed class ThrowOnErrorLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                throw new AssertFailedException($"{typeof(T).Name} logged an error: {formatter(state, exception)}", exception);
            }
        }
    }
}
