using Khadra.Application.Common;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Security;

/// <summary>
/// An endpoint a signed-in person may reach while a legal text in force waits for their consent (Wave 4, W4-8): only
/// what resolves the consent gate itself (owner, 2026-10-07) — signing out, reading who they are and what is pending,
/// accepting, and keeping the language they read the prompt in.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowWhileConsentPendingAttribute : Attribute;

/// <summary>
/// Refuses a website or console request from a signed-in person who has not accepted a legal text in force (Wave 4,
/// W4-8; D6 and D7, approved by the owner on 2026-10-07 with administrators exempt).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the server.</b> A prompt on a screen blocks only while that page is open: a tab opened before a publish, or a
/// direct call, would go straight past it. The website and the console answer this refusal with the same prompt, so the
/// person sees one thing either way.
/// </para>
/// <para>
/// <b>Who it never judges.</b> Anything anonymous — the catalogue, the legal texts themselves, <c>/app-config</c>, token
/// refresh, the payment webhook, the sandbox pages, health — whether or not a bearer came with it. An endpoint marked
/// <see cref="AllowWhileConsentPendingAttribute"/>. The customer app, by the version it declared (the advisor's review):
/// no installed build knows to ask, an installed build cannot be patched, and the app's own prompt arrives in 1.4.0.
/// Both BFFs drop the version header, so a browser can never pass itself off as the app. An administrator: the texts
/// address customers and rental offices, not Khadra's own staff, and no customer or office can claim the exemption —
/// the role is in the token the API signs, an account holds one role, and the stamp check re-reads the account on
/// every request. Background jobs make no HTTP requests at all.
/// </para>
/// <para>
/// <b>Where it sits:</b> after authorization, so a request that is unauthenticated or refused by its own policy is
/// answered by that refusal and never reaches here; and so the endpoint's metadata is known.
/// </para>
/// <para>
/// <b>No cache.</b> One indexed statement per judged request (<see cref="ILegalConsentReader.PendingAsync"/>, the one
/// statement of "pending" the prompt reads too), so a publish closes the gate at once and an acceptance opens it at once,
/// on every instance.
/// </para>
/// </remarks>
internal sealed partial class LegalConsentGate(RequestDelegate next, IClock clock, ILogger<LegalConsentGate> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var actor = context.RequestServices.GetRequiredService<ICurrentActor>();
        if (!MustJudge(context, actor) || actor.UserId is not { } userId)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var consents = context.RequestServices.GetRequiredService<ILegalConsentReader>();
        var pending = await consents.PendingAsync(userId, clock.UtcNow, context.RequestAborted).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Debug, never Information: a tab left open on the prompt polls, and every poll would be a line.
        LogRefused(logger, context.Request.Path.Value ?? "/");
        await RefuseAsync(context, pending).ConfigureAwait(false);
    }

    /// <summary>Whether this request is one the gate judges at all: see the remarks on this class.</summary>
    internal static bool MustJudge(HttpContext context, ICurrentActor actor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsAuthenticated)
            return false;

        var endpoint = context.GetEndpoint();
        if (endpoint is null
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint.Metadata.GetMetadata<AllowWhileConsentPendingAttribute>() is not null)
            return false;

        if (MobileAppVersionGate.Identify(context.Request).DeclaresAVersion)
            return false;

        return actor.Role != UserRole.Admin;
    }

    private static async Task RefuseAsync(HttpContext context, IReadOnlyList<PendingLegalVersion> pending)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status403Forbidden;
        // A refusal about THIS person's consent must never be served from a cache, to them or to anyone.
        response.Headers.CacheControl = "no-store";
        await response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = LegalErrors.ConsentPending.Message,
                Type = "https://httpstatuses.com/403",
                Instance = context.Request.Path,
                Extensions =
                {
                    ["code"] = LegalErrors.ConsentPending.Code,
                    ["traceId"] = context.TraceIdentifier,
                    // Which texts: the prompt reads its full list from /auth/me/legal-consents.
                    ["pending"] = pending
                        .Select(version => new PendingConsentProblem(version.Kind.Name, version.VersionId.Value, version.VersionLabel))
                        .ToArray(),
                },
            },
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Refused {Path} with 403 legal.consent_pending: a legal text in force is not accepted yet.")]
    private static partial void LogRefused(ILogger logger, string path);

    /// <summary>One text in the refusal's <c>pending</c> list.</summary>
    internal sealed record PendingConsentProblem(string Kind, Guid VersionId, string VersionLabel);
}
