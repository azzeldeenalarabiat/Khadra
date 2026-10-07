using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class LegalConsentRepository(KhadraDbContext context) : ILegalConsentRepository
{
    public void Add(LegalConsent consent) => context.LegalConsents.Add(consent);
}
