using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Security;

/// <summary>
/// Refuses customer-app builds older than <c>MobileApp:MinimumSupportedVersion</c>, before they reach
/// an endpoint whose contract they cannot read.
/// </summary>
/// <remarks>
/// <para>
/// **Why the server, and not the app.** The builds that most need refusing are the ones installed
/// before this existed. They have no version check of their own, so publishing a minimum they never
/// read stops nothing: only the API can turn them away, and it has to do it before authentication
/// so an old build cannot reach the contract it would crash on even once.
/// </para>
/// <para>
/// **Who is the customer app.** A request carrying <see cref="VersionHeader"/> — every build from
/// 1.1.0 sends it, with its version. Or a request whose User-Agent begins exactly
/// <see cref="LegacyUserAgentPrefix"/>, which is how builds up to 1.0.0 introduce themselves
/// (<c>Khadra (Android 16)</c>) without a version: such a build is older than any minimum, because the
/// header arrived in the release that introduced the minimum. Nothing else is ever gated — not the
/// console or the BFF (a browser cannot set that User-Agent, and YARP forwards the browser's), not a
/// health probe or the Scalar page (outside <c>/api/</c>), not server-to-server calls (.NET's
/// HttpClient sends no User-Agent at all).
/// </para>
/// <para>
/// **Where it sits:** after CORS, so a web build can read the refusal instead of seeing a CORS error,
/// and so a preflight is answered before it could be judged; after the rate limiter, so a refusal is
/// not the one path with no budget; before authentication, so an old build is told to update rather
/// than handed a 401 that sends it off to refresh a token.
/// </para>
/// <para>
/// <c>/api/v1/app-config</c> stays open to every build, deliberately: it is where a build that DOES
/// know about the minimum reads it, and refusing it there would leave that build failing call by call
/// instead of putting up its own update screen.
/// </para>
/// </remarks>
internal sealed partial class MobileAppVersionGate(
    RequestDelegate next,
    IMobileAppPolicySettings policy,
    ILogger<MobileAppVersionGate> logger)
{
    /// <summary>The header every build from 1.1.0 sends, with the version it was built as.</summary>
    public const string VersionHeader = MobileAppContract.VersionHeader;

    /// <summary>How builds up to 1.0.0 introduce themselves. Exact, and case-sensitive.</summary>
    public const string LegacyUserAgentPrefix = "Khadra (";

    /// <summary>The refusal's stable code. The app keys on this, never on the status alone.</summary>
    public const string Code = MobileAppContract.UpdateRequiredCode;

    // Arabic first, then English, separated by " · " — the convention the account emails' subjects
    // already follow. Builds up to 1.0.0 print a refusal's title verbatim and send no language to
    // choose by, so for them the title has to carry both; one short clause each, because it may be
    // shown in a snackbar.
    private const string TitleArabic = "حدّث تطبيق خضرا للمتابعة";
    private const string TitleEnglish = "Update the Khadra app to continue";
    private const string DetailArabic = "هذا الإصدار من التطبيق لم يعد مدعومًا. ثبّت أحدث إصدار من خضرا.";
    private const string DetailEnglish =
        "This version of the app is no longer supported. Install the latest version of Khadra.";

    private static readonly PathString Api = new("/api");
    private static readonly PathString AppConfig = new("/api/v1/app-config");

    // Enough to see what is being refused after a deploy, without an old build's polling filling the
    // log: a 1.0.0 phone left open refuses on every tick of its heartbeat.
    private const int RefusalsLoggedAtInformation = 20;
    private int _refusalsLogged;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var minimum = policy.MinimumSupportedVersion;
        if (minimum is null || !InScope(context.Request))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var build = Identify(context.Request);
        if (build.IsSupportedAt(minimum.Value))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        LogRefusal(build, minimum.Value, context.Request.Path);
        await RefuseAsync(context, minimum.Value).ConfigureAwait(false);
    }

    /// <summary>The API, minus the one endpoint every build must still be able to read.</summary>
    internal static bool InScope(HttpRequest request) =>
        request.Path.StartsWithSegments(Api, StringComparison.OrdinalIgnoreCase)
        && !request.Path.StartsWithSegments(AppConfig, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the request says about the build that sent it.</summary>
    internal static ClientBuild Identify(HttpRequest request)
    {
        if (request.Headers.TryGetValue(VersionHeader, out var declared))
        {
            // An unreadable version from a build that declares one is a build nothing can vouch for.
            // Refused, like an old one: letting it through would put a contract it may not read in
            // front of it, which is the whole failure this exists to prevent.
            var text = declared.Count == 1 ? declared[0]?.Trim() : null;
            return AppVersion.TryParse(text, out var version)
                ? ClientBuild.Declared(version)
                : ClientBuild.Unreadable(declared.ToString());
        }

        var userAgent = request.Headers.UserAgent.ToString();
        return userAgent.StartsWith(LegacyUserAgentPrefix, StringComparison.Ordinal)
            ? ClientBuild.Legacy(userAgent)
            : ClientBuild.NotTheApp;
    }

    private async Task RefuseAsync(HttpContext context, AppVersion minimum)
    {
        var (title, detail) = Words(context);
        var response = context.Response;
        response.StatusCode = StatusCodes.Status426UpgradeRequired;
        // A refusal about the CALLER's build must never be served from a cache to a newer one.
        response.Headers.CacheControl = "no-store";
        // No `Upgrade` header, although RFC 9110 asks for one with a 426. It is a connection-level
        // header that HTTP/2 forbids outright (RFC 9113 §8.2.2), so an edge speaking HTTP/2 to this
        // origin could treat the whole response as malformed — and there is no protocol to name, only
        // a newer app. The `code` below is the contract; the status is the conventional one for it.
        await response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status426UpgradeRequired,
                Title = title,
                Detail = detail,
                Type = "https://httpstatuses.com/426",
                Instance = context.Request.Path,
                Extensions =
                {
                    ["code"] = Code,
                    ["traceId"] = context.TraceIdentifier,
                    ["minimumSupportedVersion"] = minimum.ToString(),
                    ["updateUrl"] = policy.UpdateUrl?.AbsoluteUri,
                },
            },
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// In the language the request asked for, or in both when it asked for none — which is exactly
    /// what the builds that print this verbatim do.
    /// </summary>
    private static (string Title, string Detail) Words(HttpContext context)
    {
        var accepted = context.Request.GetTypedHeaders().AcceptLanguage;
        if (accepted is null || accepted.Count == 0)
            return ($"{TitleArabic} · {TitleEnglish}", $"{DetailArabic} · {DetailEnglish}");

        // The same rule every other localized answer on this API uses, so a build asking in Arabic
        // is answered in Arabic here too.
        var language = context.RequestServices.GetRequiredService<ICurrentLanguage>().Current;
        return language == Language.Arabic
            ? (TitleArabic, DetailArabic)
            : (TitleEnglish, DetailEnglish);
    }

    private void LogRefusal(ClientBuild build, AppVersion minimum, PathString path)
    {
        var level = Interlocked.Increment(ref _refusalsLogged) <= RefusalsLoggedAtInformation
            ? LogLevel.Information
            : LogLevel.Debug;
        if (!logger.IsEnabled(level))
            return;

        var (who, floor, where) = (build.Describe(), minimum.ToString(), path.Value ?? "/");
        if (level == LogLevel.Information)
            LogRefused(logger, who, floor, where);
        else
            LogRefusedQuietly(logger, who, floor, where);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Refused an old customer-app build ({Build}) below the minimum {Minimum} on {Path} with 426 app.update_required.")]
    private static partial void LogRefused(ILogger logger, string build, string minimum, string path);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Refused an old customer-app build ({Build}) below the minimum {Minimum} on {Path} with 426 app.update_required.")]
    private static partial void LogRefusedQuietly(ILogger logger, string build, string minimum, string path);
}

/// <summary>What a request says about the customer-app build that sent it.</summary>
/// <param name="IsTheApp">Whether the request came from the customer app at all.</param>
/// <param name="Version">The version it declared and that parsed; null for anything else.</param>
/// <param name="Evidence">What identified it, for the log.</param>
internal readonly record struct ClientBuild(bool IsTheApp, AppVersion? Version, string Evidence)
{
    public static ClientBuild NotTheApp => new(false, null, string.Empty);

    public static ClientBuild Declared(AppVersion version) => new(true, version, $"version {version}");

    public static ClientBuild Unreadable(string raw) => new(true, null, $"unreadable version \"{raw}\"");

    public static ClientBuild Legacy(string userAgent) =>
        new(true, null, $"no version header, User-Agent \"{userAgent}\"");

    /// <summary>Anything that is not the app passes; the app passes only on a version at the minimum or above.</summary>
    public bool IsSupportedAt(AppVersion minimum) =>
        !IsTheApp || (Version is { } version && version >= minimum);

    public string Describe() => IsTheApp ? Evidence : "not the customer app";
}
