namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// The administrator invited on a platform that has none (see <c>AdminBootstrapper</c>).
///
/// No <c>[Required]</c> and no validation attributes: leaving the section empty is the normal state
/// of a platform that already has an administrator, and startup must not fail because of it. The
/// values ARE validated — as an email address, a phone number and a name — but by the domain's own
/// value objects when the bootstrap runs, so there is one definition of what each of them means.
/// </summary>
public sealed class AdminBootstrapOptions
{
    public const string SectionName = "Admin:Bootstrap";

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? FullName { get; init; }
}
