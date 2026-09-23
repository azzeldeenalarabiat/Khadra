using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Khadra.Application.Notifications.Delivery;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// Which push service wakes customers' phones. <c>None</c> until one is configured.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each environment its own Firebase project</b> (owner, 2026-09-23): <c>khadra-prod</c> and
/// <c>khadra-staging</c>. A staging server holds only staging's service account, so it cannot address
/// a production phone even by mistake: FCM refuses a token that belongs to another project.
/// </para>
/// <para>
/// The service account is a secret — it signs on behalf of the whole Firebase project — and lives
/// ONLY in the environment (<c>Push__Fcm__ServiceAccountJson</c>), never in a tracked file.
/// </para>
/// </remarks>
public sealed class PushOptions
{
    public const string SectionName = "Push";
    public const string NoProvider = "None";
    public const string FcmProvider = "Fcm";

    public static readonly IReadOnlyList<string> KnownProviders = [NoProvider, FcmProvider];

    public string Provider { get; init; } = NoProvider;

    public FcmOptions Fcm { get; init; } = new();

    public bool IsFcm => string.Equals(Provider?.Trim(), FcmProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an FCM configuration can actually sign a request. Checked at startup.</summary>
    public bool FcmIsComplete =>
        !IsFcm
        || (!string.IsNullOrWhiteSpace(Fcm.ProjectId) && FcmServiceAccount.TryParse(Fcm.ServiceAccountJson, out _));
}

public sealed class FcmOptions
{
    /// <summary>The Firebase project id, e.g. <c>khadra-prod</c>. Not a secret.</summary>
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>The service-account key file's JSON, whole. A SECRET: environment only.</summary>
    public string ServiceAccountJson { get; init; } = string.Empty;
}

/// <summary>The two fields of a Google service-account key this platform uses.</summary>
internal sealed record FcmServiceAccount(string ClientEmail, string PrivateKeyPem, string TokenUri)
{
    public const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    // Never print the key.
    public override string ToString() => $"{nameof(FcmServiceAccount)} {{ ClientEmail = {ClientEmail} }}";

    public static bool TryParse(string? json, out FcmServiceAccount account)
    {
        account = null!;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var email = root.TryGetProperty("client_email", out var e) ? e.GetString() : null;
            var key = root.TryGetProperty("private_key", out var k) ? k.GetString() : null;
            var uri = root.TryGetProperty("token_uri", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key))
                return false;
            account = new FcmServiceAccount(email, key, string.IsNullOrWhiteSpace(uri) ? DefaultTokenUri : uri);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>How the notification outbox is worked. See <see cref="INotificationDeliverySettings"/>.</summary>
public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "Notifications:Delivery";

    [Range(1, 500)]
    public int BatchSize { get; init; } = 25;

    /// <summary>Long enough for a batch of pushes and emails to finish; short enough that a crash is retried soon.</summary>
    [Range(10, 3600)]
    public int LeaseSeconds { get; init; } = 120;

    [Range(1, 50)]
    public int MaxAttempts { get; init; } = 6;

    /// <summary>The first retry waits this long; each after it waits twice as long, up to the cap.</summary>
    [Range(1, 3600)]
    public int RetryBaseSeconds { get; init; } = 30;

    [Range(1, 86400)]
    public int RetryCapSeconds { get; init; } = 1800;
}

internal sealed class NotificationDeliverySettings(Microsoft.Extensions.Options.IOptions<NotificationDeliveryOptions> options)
    : INotificationDeliverySettings
{
    public int BatchSize => options.Value.BatchSize;

    public TimeSpan Lease => TimeSpan.FromSeconds(options.Value.LeaseSeconds);

    public int MaxAttempts => options.Value.MaxAttempts;

    public TimeSpan RetryDelay(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var seconds = Math.Min((double)options.Value.RetryBaseSeconds * Math.Pow(2, exponent), options.Value.RetryCapSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
