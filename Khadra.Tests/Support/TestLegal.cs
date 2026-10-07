using Khadra.Application.Common.Ports;
using Khadra.Application.Legal;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;
using NSubstitute;

namespace Khadra.Tests.Support;

/// <summary>
/// The legal texts and consents a handler test sees (Wave 4, W4-8): nothing in force unless the test publishes, and a
/// record of every consent the recorder staged.
/// </summary>
internal sealed class TestLegal
{
    public static readonly DateTimeOffset Published = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public ILegalDocumentReader Texts { get; } = Substitute.For<ILegalDocumentReader>();
    public ILegalConsentReader Consents { get; } = Substitute.For<ILegalConsentReader>();
    public ILegalConsentRepository Repository { get; } = Substitute.For<ILegalConsentRepository>();
    public ICustomerSiteSettings Site { get; } = Substitute.For<ICustomerSiteSettings>();

    /// <summary>Every consent the recorder staged, in order.</summary>
    public List<LegalConsent> Staged { get; } = [];

    private readonly List<CurrentLegalVersion> _inForce = [];

    public TestLegal()
    {
        Texts.CurrentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<CurrentLegalVersion>)[.. _inForce]);
        Consents.AlreadyAcceptedAsync(Arg.Any<Id>(), Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Id>());
        Consents.PendingAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        Consents.AcceptedAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
            .Returns([]);
        Repository.When(repository => repository.Add(Arg.Any<LegalConsent>()))
            .Do(call => Staged.Add(call.Arg<LegalConsent>()));
        Site.BaseUrl.Returns(new Uri("https://khadra.test/"));
    }

    public LegalConsentRecorder Recorder => new(Texts, Consents, Repository);

    /// <summary>Puts a version of <paramref name="kind"/> in force, and answers its id.</summary>
    public Id Publish(LegalDocumentKind kind, string label = "2026-10")
    {
        var versionId = Id.New();
        _inForce.RemoveAll(version => version.Kind == kind);
        _inForce.Add(new CurrentLegalVersion(kind, versionId, label, Published));
        return versionId;
    }

    /// <summary>The Terms and the Privacy notice, both in force: their ids, in that order.</summary>
    public (Id Terms, Id Privacy) PublishBoth() =>
        (Publish(LegalDocumentKind.Terms), Publish(LegalDocumentKind.Privacy));
}
