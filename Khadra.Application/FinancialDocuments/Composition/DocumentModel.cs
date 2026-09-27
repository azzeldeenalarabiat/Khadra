using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;

namespace Khadra.Application.FinancialDocuments.Composition;

/// <summary>
/// A text in both of the platform's languages, as a document freezes it (payments Phase 5): every word a
/// document shows is stored in English AND Arabic when it is issued, so no reader ever words it later.
/// </summary>
public sealed record BilingualText(string En, string Ar)
{
    public static BilingualText Of(string en, string ar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(en);
        ArgumentException.ThrowIfNullOrWhiteSpace(ar);
        return new BilingualText(en, ar);
    }
}

/// <summary>
/// Khadra's legal identity, as it is printed on every document (owner, 2026-09-27). Read from
/// configuration at issue and frozen into the snapshot; there is no placeholder for any part of it —
/// until every part is configured, nothing is issued (<see cref="IssuanceHoldReason.IssuerNotConfigured"/>).
/// </summary>
/// <param name="IsTestIdentity">
/// A clearly marked local test identity (owner, 2026-09-27): only ever configured in Development with
/// sandbox payments, and it never signs real money. Not written into the snapshot — the identity's own
/// words say what it is, and every document it signs is numbered TEST-.
/// </param>
public sealed record DocumentIssuer(
    BilingualText LegalName,
    string CommercialRegistration,
    BilingualText Address,
    string SupportEmail,
    string SupportPhone,
    bool IsTestIdentity = false);

/// <summary>The customer as a document names them: the name only (owner, 2026-09-27) — never the email or phone.</summary>
public sealed record CustomerParty(Id CustomerId, string Name);

/// <summary>The rental office as it stood at issue, read past the soft-delete filter.</summary>
/// <param name="City">The city's two names, when the office has a city on record.</param>
/// <param name="Area">The area as the office typed it, when it gave an address.</param>
public sealed record OfficeParty(
    Id DealerId,
    string Name,
    string CommercialRegistration,
    BilingualText? City,
    string? Area,
    string? Street);

/// <summary>The car as it stood at issue, read past the soft-delete filter.</summary>
public sealed record VehicleParty(
    Id VehicleId,
    string Make,
    string Model,
    int Year,
    string Plate,
    BilingualText? CarType);

/// <summary>Who a document is about, gathered before it is composed. Every part is required.</summary>
public sealed record DocumentParties(CustomerParty Customer, OfficeParty Office, VehicleParty Vehicle);

/// <summary>An issued document another one points at: its id and its number.</summary>
public sealed record DocumentReference(Id DocumentId, string Number);

/// <summary>A receipt a statement lists among the documents issued so far.</summary>
public sealed record ReceiptReference(FinancialDocumentType Type, Id DocumentId, string Number);

/// <summary>
/// What a document is stamped with when it is issued: its number, taken in the issuing transaction; the
/// instant; its place in its family; and whether it corrects a voided document.
/// </summary>
/// <param name="Previous">The version it supersedes, or the voided document it corrects. Null for version 1.</param>
public sealed record DocumentStamp(
    string Number,
    DateTimeOffset IssuedAt,
    int Version,
    DocumentReference? Previous,
    bool IsCorrection)
{
    /// <summary>A first version: nothing before it.</summary>
    public static DocumentStamp First(string number, DateTimeOffset issuedAt) => new(number, issuedAt, 1, null, false);
}
