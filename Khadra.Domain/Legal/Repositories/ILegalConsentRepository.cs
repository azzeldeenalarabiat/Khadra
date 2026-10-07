namespace Khadra.Domain.Legal.Repositories;

/// <summary>Stages consents for the caller's own save (Wave 4, W4-8). Append-only: there is no update and no removal.</summary>
public interface ILegalConsentRepository
{
    void Add(LegalConsent consent);
}
