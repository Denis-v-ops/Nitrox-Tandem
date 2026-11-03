using LitJson;
using Nitrox.Model.Helper;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic;

/// <summary>COOP_FEATURES_SPEC G0/G4: what each player is told while a story gate waits (L1).</summary>
[TestClass]
public class StoryGatesTest
{
    private static readonly Func<string, string> english = LoadEnglish();

    [TestMethod]
    public void Locked_NamesWhoIsOffline_ToEveryone()
    {
        StoryGateStatus status = new(StoryGateKind.SUNBEAM_RADIO, ["Alice"], ["Bob"], true);
        StoryGates.FormatStatus(status, "Alice", english).Should().Be("Waiting for Bob to come online");
        StoryGates.FormatStatus(status, "Carol", english).Should().Be("Waiting for Bob to come online");
    }

    [TestMethod]
    public void DriveCore_TheWelderWaits_ThePartnerIsCalled()
    {
        StoryGateStatus status = new(StoryGateKind.DRIVE_CORE, ["Alice"], ["Bob"], false);
        StoryGates.FormatStatus(status, "alice", english).Should().Be("Wait for Bob to finish the repair together");
        StoryGates.FormatStatus(status, "Bob", english).Should().Be("Alice is waiting for you in the drive core room to finish the repair together");
        StoryGates.FormatStatus(status, "Carol", english).Should().BeEmpty();
    }

    [TestMethod]
    public void SeaEmperor_AndHatching_SayWhereAndWhat()
    {
        StoryGateStatus platform = new(StoryGateKind.SEA_EMPEROR, ["Alice"], ["Bob"], false);
        StoryGates.FormatStatus(platform, "Alice", english).Should().Be("Waiting for Bob to join you on the platform…");
        StoryGates.FormatStatus(platform, "Bob", english).Should().Be("Alice is waiting for you on the aquarium platform");
        StoryGateStatus hatching = new(StoryGateKind.HATCHING, ["Bob"], ["Alice"], false);
        StoryGates.FormatStatus(hatching, "Bob", english).Should().Be("Waiting for Alice to use the incubator too…");
        StoryGates.FormatStatus(hatching, "Alice", english).Should().Be("Bob is ready to hatch the eggs: use the incubator too");
    }

    [TestMethod]
    public void Cure_AsksThePartnerToTakeABallToo()
    {
        StoryGateStatus cure = new(StoryGateKind.CURE, ["Alice"], ["Bob"], false);
        StoryGates.FormatStatus(cure, "Alice", english).Should().Be("Waiting for Bob to take a cure ball too…");
        StoryGates.FormatStatus(cure, "Bob", english).Should().Be("Alice is ready for the cure: take a cure ball too");
        StoryGates.FormatStatus(new StoryGateStatus(StoryGateKind.CURE, ["Alice"], ["Bob"], true), "Alice", english).Should().Be("Waiting for Bob to come online");
    }

    [TestMethod]
    public void OtherGates_UseTheGenericTexts()
    {
        StoryGateStatus status = new(StoryGateKind.DISABLE_GUN, ["Alice"], ["Bob"], false);
        StoryGates.FormatStatus(status, "Alice", english).Should().Be("Waiting for Bob…");
        StoryGates.FormatStatus(status, "Bob", english).Should().Be("Alice is waiting for you");
    }

    private static Func<string, string> LoadEnglish()
    {
        using StreamReader reader = new(Path.Combine(NitroxUser.AssetsPath, "LanguageFiles", "en.json"));
        JsonData json = JsonMapper.ToObject(reader);
        return key => (string)json[key];
    }
}
