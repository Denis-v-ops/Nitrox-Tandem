namespace Nitrox.Model.Core;

/// <summary>
///     Identity of this fork of Nitrox, so players, crash reports and update checks can tell it apart from official Nitrox.
/// </summary>
public static class NitroxFork
{
    public const string NAME = "Nitrox Tandem";
    public const string REPOSITORY_URL = "https://github.com/Denis-v-ops/Nitrox-Tandem";
    public const string ISSUES_URL = $"{REPOSITORY_URL}/issues";
    public const string RELEASES_URL = $"{REPOSITORY_URL}/releases";

    /// <summary>
    ///     True for this fork: official Nitrox releases would replace its fixes and can't load worlds it saved, so the
    ///     launcher must not offer them as updates.
    /// </summary>
    public static bool IsFork => true;

    public static bool OffersOfficialDownload => !IsFork;
}
