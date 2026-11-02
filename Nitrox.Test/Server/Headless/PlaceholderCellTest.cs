using System.Threading.Tasks;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Test.Helper.Headless;

namespace Nitrox.Test.Server.Headless;

/// <summary>
///     Placeholders that sit under an intermediate object of their prefab group (FIX_SPEC log 2026-11-02, diff finding 2):
///     the server must place them, and their cell, where the game does, when it generates the batch and after a restart.
/// </summary>
/// <remarks>
///     Uses the real game files: the KelpForest wreck near (67, -25, 394) contains group 34d3f7a1-… (a Bench and 10 other
///     placeholders at about (-780, 419, -1082) under an intermediate object). Before the fix the server put them about
///     1.4 km away, e.g. the Bench at (-832, -1120, 320) instead of (62, -39, 383) where both games had it.
/// </remarks>
[TestClass]
[TestCategory("L2")]
public class PlaceholderCellTest
{
    private const string GROUP_CLASS_ID = "34d3f7a1-3516-43c6-99f9-bf4cccc3a30b";
    private static readonly NitroxVector3 wreckPosition = new(67, -25, 394);

    [TestMethod]
    public async Task PlaceholdersUnderIntermediateObject_AreNextToTheirGroup_AlsoAfterRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-PlaceholderCell");
        await server.WorldEntityManager.LoadUnspawnedEntitiesAsync(new AbsoluteEntityCell(wreckPosition, 0).BatchId, true);

        List<PlaceholderGroupWorldEntity> groups = server.EntityRegistry.GetEntities<PlaceholderGroupWorldEntity>().Where(group => group.ClassId == GROUP_CLASS_ID).ToList();
        groups.Should().NotBeEmpty("the wreck's batch contains the group");
        AssertChildrenNextToGroups(server, groups, "when the batch is generated");

        await server.RestartAsync();

        groups = server.EntityRegistry.GetEntities<PlaceholderGroupWorldEntity>().Where(group => group.ClassId == GROUP_CLASS_ID).ToList();
        groups.Should().NotBeEmpty("the save has the group");
        AssertChildrenNextToGroups(server, groups, "after loading the save");
    }

    private static void AssertChildrenNextToGroups(ServerHarness server, List<PlaceholderGroupWorldEntity> groups, string when)
    {
        foreach (PlaceholderGroupWorldEntity group in groups)
        {
            List<PrefabPlaceholderEntity> placeholders = group.ChildEntities.OfType<PrefabPlaceholderEntity>().ToList();
            placeholders.Should().HaveCountGreaterThan(5, $"group {group.Id} has its placeholders {when}");
            foreach (PrefabPlaceholderEntity placeholder in placeholders)
            {
                NitroxVector3.Distance(placeholder.Transform.Position, group.Transform.Position).Should().BeLessThan(30, $"{placeholder.TechType} {placeholder.Id} is part of the wreck {when}");
                server.IsInCell(placeholder.Id, placeholder.AbsoluteEntityCell).Should().BeTrue($"{placeholder.Id} is registered in the cell where it is {when}");
            }
        }
    }
}
