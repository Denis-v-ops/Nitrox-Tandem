using LitJson;
using Nitrox.Model.Helper;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.Communication.Packets.Processors;

[TestClass]
public class SleepStatusUpdateProcessorTest
{
    [TestMethod]
    public void WaitingStatus_NamesWhoIsAwake()
    {
        SleepStatusUpdate packet = new(1, 2, ["Bob"]);

        SleepStatusUpdateProcessor.HasAwakePlayers(packet).Should().BeTrue();
        SleepStatusUpdateProcessor.FormatStatus(GetEnglish("Nitrox_SleepingPlayersWaitingFor"), packet)
                                  .Should().Be("1/2 players sleeping. Waiting for Bob…");
    }

    [TestMethod]
    public void WaitingStatus_ListsEveryoneAwake()
    {
        SleepStatusUpdate packet = new(1, 3, ["Bob", "Carol"]);

        SleepStatusUpdateProcessor.FormatStatus(GetEnglish("Nitrox_SleepingPlayersWaitingFor"), packet)
                                  .Should().Be("1/3 players sleeping. Waiting for Bob, Carol…");
    }

    [TestMethod]
    public void EveryoneInBed_UsesPlainCount()
    {
        SleepStatusUpdate packet = new(2, 2, []);

        SleepStatusUpdateProcessor.HasAwakePlayers(packet).Should().BeFalse();
        SleepStatusUpdateProcessor.FormatStatus(GetEnglish("Nitrox_SleepingPlayers"), packet).Should().Be("2/2 players sleeping.");
    }

    private static string GetEnglish(string key)
    {
        using StreamReader reader = new(Path.Combine(NitroxUser.AssetsPath, "LanguageFiles", "en.json"));
        JsonData json = JsonMapper.ToObject(reader);
        return (string)json[key];
    }
}
