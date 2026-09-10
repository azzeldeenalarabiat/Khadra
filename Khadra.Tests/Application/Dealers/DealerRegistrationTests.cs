using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.SubmitDealerProfile;
using Khadra.Application.Dealers.UpdateDeliverySettings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// Spec 3.1: a dealer owner submits their business, it lands PENDING_REVIEW, and nothing dealer-only
// works until an Admin approves it.
public sealed class DealerRegistrationTests
{
    private static readonly Id Owner = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public FakeDocumentStorage Storage { get; } = new();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Users.Now);
        public List<Dealer> Added { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Dealers.When(repository => repository.AddAsync(Arg.Any<Dealer>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<Dealer>()));
        }

        public ICityRepository Cities { get; } = Substitute.For<ICityRepository>();

        public SubmitDealerProfileHandler Submit() => new(
            Dealers, Cities, Storage, FakeDocumentPolicy.Default, TestBusinessRules.Provider(), Clock, UnitOfWork);

        public UpdateDeliverySettingsHandler Delivery() => new(Dealers, Clock, UnitOfWork);
    }

    private static DealerDocumentUpload Upload(string type) =>
        new(type, $"{type}.jpg", "image/jpeg", 2048, new MemoryStream([1, 2, 3, 4]));

    private static SubmitDealerProfileCommand Command(params string[] documentTypes) =>
        new(
            Owner,
            "Petra Wheels",
            "123456",
            30.3285,
            35.4444,
            new TimeOnly(8, 0),
            new TimeOnly(20, 0),
            "Tourist car hire in Petra.",
            null,
            [.. documentTypes.Select(Upload)]);

    private static SubmitDealerProfileCommand CompleteCommand() =>
        Command("CommercialRegistration", "VehicleRegistration", "OwnerIdentity");

    [Fact]
    public async Task A_submitted_dealer_starts_pending_review_and_cannot_trade()
    {
        var context = new Context();

        var result = await context.Submit().Handle(CompleteCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PendingReview", result.Value.VerificationStatus);
        Assert.False(result.Value.CanTrade);
        Assert.Empty(result.Value.MissingDocuments);
        Assert.Equal(3, result.Value.SubmittedDocuments.Count);

        var dealer = Assert.Single(context.Added);
        Assert.Equal(Owner, dealer.OwnerUserId);
        // The 48-hour SLA promise is frozen at submission, not recomputed later.
        Assert.Equal(dealer.SubmittedAt.AddHours(48), dealer.ReviewDueAt);
    }

    [Fact]
    public async Task Every_uploaded_document_reaches_storage_under_the_dealers_own_scope()
    {
        var context = new Context();

        await context.Submit().Handle(CompleteCommand(), CancellationToken.None);

        Assert.Equal(3, context.Storage.Saved.Count);
        Assert.All(context.Storage.Saved, saved => Assert.StartsWith("dealers/", saved.Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_incomplete_submission_is_refused_before_it_reaches_an_admin()
    {
        // Spec 3.1 names all three documents. Letting a partial application through only wastes the
        // 48-hour SLA on something an Admin can do nothing with.
        var context = new Context();

        var result = await context.Submit().Handle(
            Command("CommercialRegistration", "OwnerIdentity"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.missing_documents", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task An_owner_cannot_register_a_second_business()
    {
        var context = new Context();
        context.Dealers.ExistsForOwnerAsync(Owner, Arg.Any<CancellationToken>()).Returns(true);

        var result = await context.Submit().Handle(CompleteCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.already_registered", result.Error.Code);
    }

    [Fact]
    public async Task A_commercial_registration_number_cannot_be_reused()
    {
        var context = new Context();
        context.Dealers
            .CommercialRegistrationExistsAsync(Arg.Any<CommercialRegistrationNumber>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.Submit().Handle(CompleteCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.commercial_registration_taken", result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_document_type_is_rejected_before_anything_is_written()
    {
        var context = new Context();

        var result = await context.Submit().Handle(Command("TaxCertificate"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.unsupported_document_type", result.Error.Code);
        Assert.Empty(context.Storage.Saved);
    }

    [Fact]
    public async Task An_oversized_or_wrongly_typed_file_never_reaches_storage()
    {
        var context = new Context();

        var tooBig = new SubmitDealerProfileCommand(
            Owner, "Petra Wheels", "123456", 30.3285, 35.4444,
            new TimeOnly(8, 0), new TimeOnly(20, 0), null, null,
            [new DealerDocumentUpload("CommercialRegistration", "big.jpg", "image/jpeg", 50_000_000, new MemoryStream())]);
        Assert.Equal("dealer.document_too_large",
            (await context.Submit().Handle(tooBig, CancellationToken.None)).Error.Code);

        var wrongType = new SubmitDealerProfileCommand(
            Owner, "Petra Wheels", "123456", 30.3285, 35.4444,
            new TimeOnly(8, 0), new TimeOnly(20, 0), null, null,
            [new DealerDocumentUpload("CommercialRegistration", "run.exe", "application/x-msdownload", 2048, new MemoryStream())]);
        Assert.Equal("dealer.invalid_document_content",
            (await context.Submit().Handle(wrongType, CancellationToken.None)).Error.Code);

        Assert.Empty(context.Storage.Saved);
    }

    [Fact]
    public async Task A_pending_dealer_is_blocked_from_a_dealer_only_action()
    {
        // The whole point of PENDING_REVIEW: the account exists, the application exists, and nothing
        // operational is permitted yet.
        var context = new Context();
        var dealer = Build.Dealer(ownerUserId: Owner);
        context.Dealers.GetByOwnerUserIdAsync(Owner, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await context.Delivery().Handle(
            new UpdateDeliverySettingsCommand(Owner, IsEnabled: true, RadiusKm: 30m, Fee: 8m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.not_approved", result.Error.Code);
        Assert.Equal(Khadra.Domain.Common.ErrorKind.Forbidden, result.Error.Kind);
    }

    [Fact]
    public async Task An_approved_dealer_may_perform_the_same_action()
    {
        var context = new Context();
        var dealer = Build.ApprovedDealer(ownerUserId: Owner);
        context.Dealers.GetByOwnerUserIdAsync(Owner, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await context.Delivery().Handle(
            new UpdateDeliverySettingsCommand(Owner, IsEnabled: true, RadiusKm: 30m, Fee: 8m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanTrade);
    }

    [Fact]
    public async Task A_suspended_but_approved_dealer_is_blocked_too()
    {
        // Verification and suspension are independent; CanTrade needs both to be right.
        var context = new Context();
        var dealer = Build.ApprovedDealer(ownerUserId: Owner);
        dealer.Suspend(Id.New(), "Policy violation.", Users.Now);
        context.Dealers.GetByOwnerUserIdAsync(Owner, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await context.Delivery().Handle(
            new UpdateDeliverySettingsCommand(Owner, IsEnabled: true, RadiusKm: 30m, Fee: 8m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.not_approved", result.Error.Code);
    }
}

/// <summary>
/// The address the owner records, and the city they file the gallery under.
/// </summary>
/// <remarks>
/// Both arrive from the same form as the pin, in one write. The reverse geocoder can OFFER values
/// into that form through its own read endpoint, but it is deliberately absent from this handler's
/// constructor: what is stored is what the owner confirmed, so a provider that is throttled, wrong
/// about Jordan, or simply down cannot change what an administrator later checks against a licence.
/// </remarks>
public sealed class DealerApplicationLocationTests
{
    private static readonly Id Owner = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public ICityRepository Cities { get; } = Substitute.For<ICityRepository>();
        public IDocumentStorage Storage { get; } = new FakeDocumentStorage();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Build.Now);
        public List<Dealer> Added { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Dealers.When(repository => repository.AddAsync(Arg.Any<Dealer>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<Dealer>()));
        }

        public SubmitDealerProfileHandler Submit() => new(
            Dealers, Cities, Storage, FakeDocumentPolicy.Default, TestBusinessRules.Provider(), Clock, UnitOfWork);
    }

    private static DealerDocumentUpload Upload(string type) =>
        new(type, $"{type}.jpg", "image/jpeg", 2048, new MemoryStream([1, 2, 3, 4]));

    private static SubmitDealerProfileCommand Command(
        Id? cityId = null,
        string? area = null,
        string? street = null) =>
        new(
            Owner,
            "Petra Wheels",
            "123456",
            31.9539,
            35.9106,
            new TimeOnly(8, 0),
            new TimeOnly(20, 0),
            "Tourist car hire.",
            cityId,
            [.. new[] { "CommercialRegistration", "VehicleRegistration", "OwnerIdentity" }.Select(Upload)],
            area,
            street);

    [Fact]
    public async Task The_address_is_stored_exactly_as_the_owner_wrote_it()
    {
        var context = new Context();

        var result = await context.Submit().Handle(
            Command(area: "  Abdoun ", street: "Al-Kindi Street"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var dealer = Assert.Single(context.Added);
        Assert.NotNull(dealer.Address);
        // Tidied, not rewritten: the owner's words, with the stray spacing collapsed.
        Assert.Equal("Abdoun", dealer.Address!.Area);
        Assert.Equal("Al-Kindi Street", dealer.Address.Street);
    }

    /// <summary>An application without an address is complete; the pin is the location.</summary>
    [Fact]
    public async Task An_application_with_no_address_is_still_accepted()
    {
        var context = new Context();

        var result = await context.Submit().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Null(Assert.Single(context.Added).Address);
    }

    /// <summary>A street with no area is not half an address; it is an unusable one.</summary>
    [Fact]
    public async Task A_street_with_no_area_is_refused_before_anything_is_written()
    {
        var context = new Context();

        var result = await context.Submit().Handle(
            Command(street: "Al-Kindi Street"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.invalid_address_area", result.Error.Code);
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// The regression. The city id arrived from a dropdown and was stored exactly as sent, so a
    /// stale or tampered id was accepted and then quietly excluded the gallery from its own city
    /// filter forever -- the catalogue matches on CityId, and nothing anywhere reported the mismatch.
    /// </summary>
    [Fact]
    public async Task A_city_that_does_not_exist_is_refused()
    {
        var context = new Context();
        context.Cities.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns((City?)null);

        var result = await context.Submit().Handle(Command(cityId: Id.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.unknown_city", result.Error.Code);
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// An inactive city is refused as firmly as a missing one: an administrator retired it, and a
    /// new gallery should not be filed under a heading the platform has stopped using.
    /// </summary>
    [Fact]
    public async Task A_retired_city_is_refused()
    {
        var context = new Context();
        var city = City.Create("Amman", "عمان", 1, Build.Now, null).Value;
        city.Deactivate();
        context.Cities.GetByIdAsync(city.Id, Arg.Any<CancellationToken>()).Returns(city);

        var result = await context.Submit().Handle(Command(cityId: city.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.unknown_city", result.Error.Code);
    }

    [Fact]
    public async Task An_active_city_is_recorded_on_the_gallery()
    {
        var context = new Context();
        var city = City.Create("Amman", "عمان", 1, Build.Now, null).Value;
        context.Cities.GetByIdAsync(city.Id, Arg.Any<CancellationToken>()).Returns(city);

        var result = await context.Submit().Handle(Command(cityId: city.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(city.Id, Assert.Single(context.Added).CityId);
    }
}
