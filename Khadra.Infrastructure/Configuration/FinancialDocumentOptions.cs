using System.ComponentModel.DataAnnotations;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.ReadModels;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// How the platform issues financial documents (payments Phase 5): whose name is on them, and how the
/// settlement pass paces the work. None of it changes a figure on any document.
/// </summary>
public sealed class FinancialDocumentOptions
{
    public const string SectionName = "FinancialDocuments";

    /// <summary>Khadra's legal identity, printed on every document. Empty until the owner configures it.</summary>
    public FinancialDocumentIssuerOptions Issuer { get; init; } = new();

    /// <summary>The most documents one settlement pass issues or holds; the rest wait for the next pass.</summary>
    [Range(1, 1000)]
    public int MaxDocumentsPerPass { get; init; } = 200;

    /// <summary>The most PDF renditions one settlement pass draws (payments Phase 6); the rest wait for the next pass.</summary>
    [Range(1, 1000)]
    public int MaxRenditionsPerPass { get; init; } = 40;

    /// <summary>The first wait after a document family is put on hold; each further failure doubles it.</summary>
    [Range(1, 86_400)]
    public int RetryInitialSeconds { get; init; } = 60;

    /// <summary>The longest wait between two attempts at a family on hold.</summary>
    [Range(1, 604_800)]
    public int RetryMaxSeconds { get; init; } = 3_600;

    /// <summary>
    /// How long a newly issued statement's booking is looked at again, for a fact committed just after the
    /// statement read the records but stamped with an earlier instant. The fingerprint decides.
    /// </summary>
    [Range(1, 1_440)]
    public int LateCommitMarginMinutes { get; init; } = 10;
}

/// <summary>
/// Khadra's legal identity for issued documents (owner, 2026-09-27): ALL of it, or none of it. Nothing is
/// issued with placeholder or incomplete issuer information, so an empty section holds every document on
/// hold (<c>IssuerNotConfigured</c>) and a half-filled one refuses to start.
/// </summary>
/// <remarks>
/// Not a secret — it is printed on every receipt — but it lives in the environment or user-secrets until
/// the owner decides it, and the tracked settings hold empty values.
/// </remarks>
public sealed class FinancialDocumentIssuerOptions
{
    public const int MaxLength = 200;

    public string? LegalNameEn { get; init; }
    public string? LegalNameAr { get; init; }
    public string? CommercialRegistration { get; init; }
    public string? AddressEn { get; init; }
    public string? AddressAr { get; init; }
    public string? SupportEmail { get; init; }
    public string? SupportPhone { get; init; }

    /// <summary>
    /// A clearly marked local test identity (owner, 2026-09-27): allowed ONLY in Development with the
    /// SANDBOX payment provider — <c>Program.cs</c> refuses to start anything else with it — and it never
    /// signs real money even there. Every document it signs is sandbox money, so every number is TEST-.
    /// </summary>
    public bool TestIdentity { get; init; }

    private string?[] Parts => [LegalNameEn, LegalNameAr, CommercialRegistration, AddressEn, AddressAr, SupportEmail, SupportPhone];

    /// <summary>Nothing configured: documents are held, and the boot log says so.</summary>
    public bool IsEmpty => Parts.All(string.IsNullOrWhiteSpace) && !TestIdentity;

    /// <summary>Every part present.</summary>
    public bool IsComplete => Parts.All(part => !string.IsNullOrWhiteSpace(part));

    /// <summary>Either nothing or everything, each part within its length and the email an address.</summary>
    public bool IsValid =>
        IsEmpty
        || (IsComplete
            && Parts.All(part => part!.Trim().Length <= MaxLength)
            && new EmailAddressAttribute().IsValid(SupportEmail!.Trim()));

    /// <summary>The settings a partial identity is missing, named so a deploy log says which to set.</summary>
    public IReadOnlyList<string> Missing()
    {
        var named = new (string Name, string? Value)[]
        {
            (nameof(LegalNameEn), LegalNameEn),
            (nameof(LegalNameAr), LegalNameAr),
            (nameof(CommercialRegistration), CommercialRegistration),
            (nameof(AddressEn), AddressEn),
            (nameof(AddressAr), AddressAr),
            (nameof(SupportEmail), SupportEmail),
            (nameof(SupportPhone), SupportPhone),
        };
        return [.. named.Where(part => string.IsNullOrWhiteSpace(part.Value)).Select(part => $"{FinancialDocumentOptions.SectionName}:Issuer:{part.Name}")];
    }
}

/// <summary>
/// Refuses a half-configured issuer at startup, naming each missing setting: a deploy log that says
/// "FinancialDocuments:Issuer:AddressAr is not set" ends the search that "the issuer is misconfigured" starts.
/// </summary>
internal sealed class FinancialDocumentIssuerValidator : IValidateOptions<FinancialDocumentOptions>
{
    public ValidateOptionsResult Validate(string? name, FinancialDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var issuer = options.Issuer;
        if (issuer.IsValid)
            return ValidateOptionsResult.Success;

        if (!issuer.IsComplete)
        {
            return ValidateOptionsResult.Fail(
                "FinancialDocuments:Issuer is partly configured. No document is issued with incomplete issuer "
                + $"information, so set every part or none. Missing: {string.Join(", ", issuer.Missing())}.");
        }

        return ValidateOptionsResult.Fail(
            $"FinancialDocuments:Issuer: each part is at most {FinancialDocumentIssuerOptions.MaxLength} characters, "
            + "and SupportEmail must be an email address.");
    }
}

/// <summary>Reads <see cref="FinancialDocumentOptions"/> as the application layer's port.</summary>
internal sealed class FinancialDocumentSettings(IOptions<FinancialDocumentOptions> options) : IFinancialDocumentSettings
{
    public DocumentIssuer? Issuer
    {
        get
        {
            var issuer = options.Value.Issuer;
            if (!issuer.IsComplete)
                return null;

            return new DocumentIssuer(
                BilingualText.Of(issuer.LegalNameEn!.Trim(), issuer.LegalNameAr!.Trim()),
                issuer.CommercialRegistration!.Trim(),
                BilingualText.Of(issuer.AddressEn!.Trim(), issuer.AddressAr!.Trim()),
                issuer.SupportEmail!.Trim(),
                issuer.SupportPhone!.Trim(),
                issuer.TestIdentity);
        }
    }

    public int MaxDocumentsPerPass => options.Value.MaxDocumentsPerPass;

    public int MaxRenditionsPerPass => options.Value.MaxRenditionsPerPass;

    public TimeSpan RetryInitialDelay => TimeSpan.FromSeconds(options.Value.RetryInitialSeconds);

    public TimeSpan RetryMaxDelay => TimeSpan.FromSeconds(options.Value.RetryMaxSeconds);

    public TimeSpan LateCommitMargin => TimeSpan.FromMinutes(options.Value.LateCommitMarginMinutes);
}
