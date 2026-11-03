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
    public void DisableGun_BothUseTheTerminal()
    {
        StoryGateStatus status = new(StoryGateKind.DISABLE_GUN, ["Alice"], ["Bob"], false);
        StoryGates.FormatStatus(status, "Alice", english).Should().Be("Waiting for Bob to use the terminal too");
        StoryGates.FormatStatus(status, "Bob", english).Should().Be("Alice is at the gun terminal: use it together to disable the gun");
    }

    [TestMethod]
    public void RocketLaunch_BothPressLaunchAboard()
    {
        StoryGateStatus status = new(StoryGateKind.ROCKET_LAUNCH, ["Alice"], ["Bob"], false);
        StoryGates.FormatStatus(status, "Alice", english).Should().Be("Waiting for Bob to board the cockpit and press launch");
        StoryGates.FormatStatus(status, "Bob", english).Should().Be("Alice is ready for launch: board the rocket's cockpit and press launch too");
    }

    [TestMethod]
    public void OtherGates_UseTheGenericTexts()
    {
        StoryGateStatus status = new(StoryGateKind.SUNBEAM_RADIO, ["Alice"], ["Bob"], false);
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
