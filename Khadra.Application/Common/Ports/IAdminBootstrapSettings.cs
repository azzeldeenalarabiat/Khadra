namespace Khadra.Application.Common.Ports;

/// <summary>
/// Who receives the very first administrator invitation. Bound from configuration by Infrastructure.
/// </summary>
/// <remarks>
/// Deliberately an address, a phone and a name — and deliberately NOT a password. A password here
/// would be a working credential sitting in a file or an environment variable, on the one account
/// the "last administrator" guard keeps alive for ever, and it would defeat the invariant
/// <c>User.CreateInvitedAdmin</c> exists to hold: that accepting the emailed link is what proves the
/// mailbox belongs to the person using the account.
/// </remarks>
public interface IAdminBootstrapSettings
{
    /// <summary>Empty when no bootstrap is configured, which is a valid state and a no-op.</summary>
    string? Email { get; }

    string? Phone { get; }

    string? FullName { get; }
}
