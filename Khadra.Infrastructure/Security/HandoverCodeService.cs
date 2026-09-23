using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Bookings.Handover;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

/// <summary>
/// Six-digit handover codes, and the keyed hash that is all the platform keeps of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The key is derived, not configured.</b> HKDF over the API's JWT signing key, with a label of
/// its own, gives a key that is secret wherever that one is — every environment already guards it —
/// and is useless for anything but this, so neither secret can be used to forge the other. Rotating
/// the signing key invalidates live codes, which live fifteen minutes; the customer asks for a new one.
/// </para>
/// <para>
/// <b>Bound to the booking and the handover type</b>: both are inside the MAC, so a code valid for one
/// booking's pickup is worthless for anything else, even to someone holding a copy of the table.
/// </para>
/// </remarks>
internal sealed class HandoverCodeService(IOptions<JwtOptions> jwt) : IHandoverCodeService
{
    private static readonly byte[] Label = Encoding.UTF8.GetBytes("khadra:handover-code:v1");

    private readonly byte[] _key = HKDF.DeriveKey(
        HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(jwt.Value.SigningKey),
        outputLength: 32,
        salt: [],
        info: Label);

    public string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public string Hash(Id bookingId, HandoverType type, string code)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(code);
        var message = Encoding.UTF8.GetBytes($"{bookingId.Value:N}|{type.Name}|{code.Trim()}");
        return Convert.ToHexString(HMACSHA256.HashData(_key, message)).ToLowerInvariant();
    }

    public bool Matches(string storedHash, Id bookingId, HandoverType type, string presentedCode)
    {
        ArgumentNullException.ThrowIfNull(storedHash);
        // Only digits can ever match; anything else is compared anyway, so the time taken says nothing.
        var presented = Hash(bookingId, type, presentedCode ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(presented),
            Encoding.ASCII.GetBytes(storedHash.Trim().ToLowerInvariant()));
    }
}

/// <summary>How handovers are verified. See <see cref="IHandoverSettings"/>.</summary>
public sealed class HandoverOptions
{
    public const string SectionName = "Handover";

    /// <summary>
    /// Off until the customer app that shows codes (1.2.0) is published. Then on, in the same step as
    /// raising MobileApp:MinimumSupportedVersion to 1.2.0 — see docs/production.md.
    /// </summary>
    public bool RequireVerification { get; init; }

    [Range(1, 240)]
    public int CodeLifetimeMinutes { get; init; } = 15;

    [Range(1, 20)]
    public int MaxFailedAttempts { get; init; } = 5;
}

internal sealed class HandoverSettings(IOptions<HandoverOptions> options) : IHandoverSettings
{
    public bool RequireVerification => options.Value.RequireVerification;

    public TimeSpan CodeLifetime => TimeSpan.FromMinutes(options.Value.CodeLifetimeMinutes);

    public int MaxFailedAttempts => options.Value.MaxFailedAttempts;
}
