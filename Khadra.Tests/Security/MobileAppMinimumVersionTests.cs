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

    [Fact]
    public void The_minimum_this_release_ships_with_keeps_the_installed_1_0_0_build_out()
    {
        // The reason the minimum exists today: 1.0.0 casts the bilingual office texts to strings and
        // throws. Should someone lower the shipped minimum to let it back in, this says why not.
        using var settings = JsonDocument.Parse(
            File.ReadAllText(RepositoryRoot.File("Khadra.WebAPI", "appsettings.json")), Relaxed);
        var configured = settings.RootElement.GetProperty("MobileApp").GetProperty("MinimumSupportedVersion").GetString();

        Assert.True(AppVersion.TryParse(configured, out var minimum));
        Assert.True(AppVersion.TryParse("1.0.0", out var installed));
        Assert.True(installed < minimum,
            "1.0.0 cannot read { text, language } and must stay refused until it is gone from phones.");
    }

    /// <summary>The rules a build learned (MobileAppContract), each with the first version that answers it.</summary>
    public static TheoryData<string, string> RulesABuildLearned() => new()
    {
        { nameof(MobileAppContract.ConsentAwareFrom), MobileAppContract.ConsentAwareFrom.ToString() },
        { nameof(MobileAppContract.HandoverWindowAwareFrom), MobileAppContract.HandoverWindowAwareFrom.ToString() },
        { nameof(MobileAppContract.DisputeSharesWithheldFrom), MobileAppContract.DisputeSharesWithheldFrom.ToString() },
    };

    /// <remarks>
    /// The temporary bridge spares an app that DECLARES a version older than the rule (pre-launch item 239). A rule
    /// set above the app in this repository would spare the very build written to answer it, and every phone-side
    /// test of that rule would pass against an API that never asks.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RulesABuildLearned))]
    public void The_app_in_this_repository_is_never_spared_a_rule_it_was_built_to_answer(string rule, string from)
    {
        Assert.True(AppVersion.TryParse(from, out var threshold));
        var app = AppBuildVersion();
        Assert.True(threshold <= app,
            $"MobileAppContract.{rule} is {threshold}, above Khadra.Mobile/pubspec.yaml's {app}: the app in this "
            + "repository declares an older version than the rule and is spared it.");
    }

    /// <remarks>
    /// With the tracked minimum at or above every rule, no build the API admits is spared any of them: the bridge is
    /// inert wherever the tracked setting is in force, and only a host's own lower override (Staging until the phone
    /// check, owner 2026-10-09) keeps it open.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RulesABuildLearned))]
    public void The_tracked_minimum_admits_no_build_older_than_the_rules(string rule, string from)
    {
        using var settings = JsonDocument.Parse(
            File.ReadAllText(RepositoryRoot.File("Khadra.WebAPI", "appsettings.json")), Relaxed);
        var configured = settings.RootElement.GetProperty("MobileApp").GetProperty("MinimumSupportedVersion").GetString();

        Assert.True(AppVersion.TryParse(configured, out var minimum));
        Assert.True(AppVersion.TryParse(from, out var threshold));
        Assert.True(minimum >= threshold,
            $"The tracked minimum {minimum} admits builds older than MobileAppContract.{rule} ({threshold}), which the "
            + "API spares that rule.");
    }

    [GeneratedRegex(@"^version:\s*(?<version>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PubspecVersion();
}
