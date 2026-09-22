using System.Text.Json;
using System.Text.RegularExpressions;
using Khadra.Application.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Security;

/// <summary>
/// The repository never refuses its own customer app.
/// </summary>
/// <remarks>
/// The failure this catches is the one nothing else would notice until it was in production: the
/// minimum raised in the API's settings without the app's own version being raised with it. Every
/// test would pass, the API would start, and every phone — including the build that shipped in the
/// same change — would open on the update screen with nothing to update to.
/// </remarks>
public sealed partial class MobileAppMinimumVersionTests
{
    private static readonly JsonDocumentOptions Relaxed = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AppVersion AppBuildVersion()
    {
        var pubspec = File.ReadAllText(RepositoryRoot.File("Khadra.Mobile", "pubspec.yaml"));
        var declared = PubspecVersion().Match(pubspec);
        Assert.True(declared.Success, "Khadra.Mobile/pubspec.yaml has no `version:` line.");
        Assert.True(AppVersion.TryParse(declared.Groups["version"].Value, out var version),
            $"pubspec version \"{declared.Groups["version"].Value}\" is not a semantic version.");
        return version;
    }

    /// <summary>Every tracked settings file that could carry a minimum into a running API.</summary>
    public static TheoryData<string> SettingsFiles() =>
    [
        "appsettings.json",
        "appsettings.Development.json",
        "appsettings.Local.example.json",
    ];

    [Theory]
    [MemberData(nameof(SettingsFiles))]
    public void No_settings_file_raises_the_minimum_above_the_app_in_this_repository(string file)
    {
        var path = RepositoryRoot.File("Khadra.WebAPI", file);
        using var settings = JsonDocument.Parse(File.ReadAllText(path), Relaxed);

        if (!settings.RootElement.TryGetProperty("MobileApp", out var section)
            || !section.TryGetProperty("MinimumSupportedVersion", out var configured)
            || string.IsNullOrWhiteSpace(configured.GetString()))
            return; // This file sets no minimum; nothing it could refuse.

        Assert.True(AppVersion.TryParse(configured.GetString(), out var minimum),
            $"{file}: MobileApp:MinimumSupportedVersion \"{configured.GetString()}\" is not a semantic version.");

        var app = AppBuildVersion();
        Assert.True(minimum <= app,
            $"{file} refuses customer-app builds below {minimum}, but Khadra.Mobile/pubspec.yaml is {app}: "
            + "the app in this very repository would be told to update. Raise the app's version in the same change.");
    }

    [GeneratedRegex(@"^version:\s*(?<version>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PubspecVersion();
}
