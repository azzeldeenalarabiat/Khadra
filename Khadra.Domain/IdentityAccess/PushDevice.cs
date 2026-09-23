using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

/// <summary>Which push service a device token belongs to.</summary>
public sealed class PushPlatform : Enumeration
{
    public static readonly PushPlatform Android = new(1, "Android");
    public static readonly PushPlatform Ios = new(2, "Ios");

    private PushPlatform(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// One phone that can be woken for one signed-in person: its push token, and the session it is tied to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not on the refresh token.</b> A refresh token row is replaced on every rotation, so a push
/// token stored there would be copied forward on every refresh or lost. The session FAMILY is the
/// stable thing — one per sign-in on one device — so this row points at it.
/// </para>
/// <para>
/// <b>Keyed by the token, not by the person.</b> The token identifies the app install. When a phone
/// signs out and somebody else signs in on it, the same token re-registers and the row moves to the
/// new person and session; it never stays with the old one, which is what would push somebody
/// else's booking to a phone that has changed hands.
/// </para>
/// <para>
/// <b>Revoked is not the only guard.</b> Delivery also requires the tied session to still be live
/// (see <c>IPushDeviceRepository.ListDeliverableAsync</c>), so a sign-out path that forgets to revoke
/// this row still cannot push to a signed-out phone.
/// </para>
/// </remarks>
public sealed class PushDevice : AggregateRoot
{
    /// <summary>
    /// FCM tokens are about 160 characters today. The cap leaves room for growth and stays well under
    /// what PostgreSQL can hold in the unique B-tree index on this column (about 2.7 KB).
    /// </summary>
    public const int MaxTokenLength = 1024;

    public const int MaxAppVersionLength = 32;

    public string Token { get; private set; } = null!;
    public PushPlatform Platform { get; private set; } = null!;
    public Id UserId { get; private set; }

    /// <summary>The refresh-token family this device signed in as. Null for a token issued before sessions carried one.</summary>
    public Guid? SessionFamilyId { get; private set; }

    /// <summary>The language the app is showing, so a push reads the same as the screen it opens.</summary>
    public Language Language { get; private set; } = null!;

    public string? AppVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    private PushDevice()
    {
    }

    private PushDevice(Id id) : base(id)
    {
    }

    public static PushDevice Register(
        string token,
        PushPlatform platform,
        Id userId,
        Guid? sessionFamilyId,
        Language language,
        string? appVersion,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(language);
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            throw new DomainException("A push token is required and must fit the column.");
        if (userId.IsEmpty)
            throw new DomainException("A push device belongs to a user.");

        return new PushDevice(Id.New())
        {
            Token = token,
            Platform = platform,
            UserId = userId,
            SessionFamilyId = sessionFamilyId,
            Language = language,
            AppVersion = Truncate(appVersion, MaxAppVersionLength),
            CreatedAt = now,
            LastSeenAt = now,
        };
    }

    /// <summary>
    /// The same install registering again: after a token refresh, a language switch, an app update,
    /// or a different person signing in on this phone. Whoever registers last owns it.
    /// </summary>
    public void Reregister(
        PushPlatform platform,
        Id userId,
        Guid? sessionFamilyId,
        Language language,
        string? appVersion,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(language);
        if (userId.IsEmpty)
            throw new DomainException("A push device belongs to a user.");

        Platform = platform;
        UserId = userId;
        SessionFamilyId = sessionFamilyId;
        Language = language;
        AppVersion = Truncate(appVersion, MaxAppVersionLength);
        LastSeenAt = now;
        RevokedAt = null;
    }

    /// <summary>Stops pushes to this install: signed out, or the push service said the token is dead.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
