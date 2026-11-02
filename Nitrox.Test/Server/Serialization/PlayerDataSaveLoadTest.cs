using Newtonsoft.Json.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Players;
using Nitrox.Server.Subnautica.Models.GameLogic.Unlockables;
using Nitrox.Server.Subnautica.Models.Serialization.World;

namespace Nitrox.Server.Subnautica.Models.Serialization;

/// <summary>
///     The co-op player fields (COOP_FEATURES_SPEC A1: T1's rescue state, D5b's heard goals) in <c>PlayerData.json</c>.
/// </summary>
[TestClass]
public class PlayerDataSaveLoadTest
{
    [TestMethod]
    public void CoopFieldsSurviveSaveAndLoad()
    {
        Player alice = CreatePlayer("Alice");
        alice.DownedUntil = 1234.5;
        alice.DownsSinceRest = 2;
        alice.LastDownedAt = 1174.25;
        alice.HeardGoals.Add("Goal_BiomeKelpForest");
        alice.HeardGoals.Add("Goal_LocationAuroraEntry");
        alice.Interfered = true;
        Player bob = CreatePlayer("Bob");

        PersistedWorldData loaded = SaveLoadRoundTrip.SaveAndLoad(CreateWorld(PlayerData.From([alice, bob])));

        List<Player> players = loaded.PlayerData.GetPlayers();
        Player loadedAlice = players.Single(player => player.Name == "Alice");
        loadedAlice.DownedUntil.Should().Be(1234.5);
        loadedAlice.DownsSinceRest.Should().Be(2);
        loadedAlice.LastDownedAt.Should().Be(1174.25);
        loadedAlice.HeardGoals.Should().BeEquivalentTo("Goal_BiomeKelpForest", "Goal_LocationAuroraEntry");
        loadedAlice.Interfered.Should().BeFalse("the interference flag is kept in memory only");
        AssertNoCoopState(players.Single(player => player.Name == "Bob"));
    }

    /// <summary>
    ///     A save written before these fields existed loads without an upgrade step.
    /// </summary>
    [TestMethod]
    public void SaveWithoutCoopFieldsLoadsWithDefaults()
    {
        ServerJsonSerializer serializer = new(new SaveLoadRoundTrip.ThrowOnErrorLogger<ServerJsonSerializer>());
        Player alice = CreatePlayer("Alice");
        alice.DownedUntil = 1234.5;
        alice.DownsSinceRest = 2;
        alice.LastDownedAt = 1174.25;
        alice.HeardGoals.Add("Goal_BiomeKelpForest");
        using MemoryStream stream = new();
        serializer.Serialize(stream, PlayerData.From([alice]));
        JObject json = JObject.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        JObject savedPlayer = (JObject)json["Players"]![0]!;
        foreach (string field in new[] { nameof(PersistedPlayerData.DownedUntil), nameof(PersistedPlayerData.DownsSinceRest), nameof(PersistedPlayerData.LastDownedAt), nameof(PersistedPlayerData.HeardGoals) })
        {
            savedPlayer.Remove(field).Should().BeTrue($"{field} should be in the saved player");
        }

        using MemoryStream oldSave = new(System.Text.Encoding.UTF8.GetBytes(json.ToString()));
        PlayerData loaded = serializer.Deserialize<PlayerData>(oldSave);

        Player loadedAlice = loaded.GetPlayers().Should().ContainSingle().Subject;
        loadedAlice.Name.Should().Be("Alice");
        AssertNoCoopState(loadedAlice);
    }

    private static void AssertNoCoopState(Player player)
    {
        player.DownedUntil.Should().BeNull();
        player.DownsSinceRest.Should().Be(0);
        player.LastDownedAt.Should().BeNull();
        player.HeardGoals.Should().BeEmpty();
        player.Interfered.Should().BeFalse();
    }

    private static Player CreatePlayer(string name)
    {
        return new PersistedPlayerData
        {
            Name = name,
            Id = (uint)name.Length,
            NitroxId = new NitroxId(),
            SpawnPosition = new NitroxVector3(1, -2, 3),
            SpawnRotation = NitroxQuaternion.Identity,
            CurrentStats = new PlayerStatsData(45, 45, 80, 50, 90, 0.1f),
            Permissions = Perms.PLAYER,
            PlayerPreferences = new SubnauticaPlayerPreferences([], [])
        }.ToPlayer();
    }

    private static PersistedWorldData CreateWorld(PlayerData playerData)
    {
        return new PersistedWorldData
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
            PlayerData = playerData,
            GlobalRootData = GlobalRootData.From([]),
            EntityData = EntityData.From([])
        };
    }
}
