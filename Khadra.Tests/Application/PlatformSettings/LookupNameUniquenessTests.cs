using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.PlatformSettings.Lookups;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using Khadra.Tests.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Application.PlatformSettings;

/// <summary>
/// One offered name per list (pre-launch item 52): the handler's check, and what its caller is told when the check
/// loses a race to the database's partial unique index. The index itself is proved on PostgreSQL in
/// <c>PostgresLookupNameIndexTests</c>.
/// </summary>
public sealed class LookupNameUniquenessTests
{
    private readonly ICityRepository _cities = Substitute.For<ICityRepository>();
    private readonly ICarTypeRepository _carTypes = Substitute.For<ICarTypeRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private LookupHandlers Handlers()
    {
        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(Id.New());
        actor.Role.Returns(UserRole.Admin);
        actor.Name.Returns("Rania Haddad");
        return new LookupHandlers(
            _carTypes, _cities, new AdminActionRecorder(Substitute.For<IAuditTrail>(), actor, new TestClock(Build.Now)), _unitOfWork,
            new TestClock(Build.Now));
    }

    private void Offered(params City[] cities) =>
        _cities.ListAsync(activeOnly: true, Arg.Any<CancellationToken>()).Returns(cities);

    [Fact]
    public async Task A_name_another_offered_city_uses_is_refused_with_its_marks_and_case_folded()
    {
        Offered(City.Create("Madaba", "مادبا", 1, Build.Now).Value);

        var english = await Handlers().Handle(new CreateCityCommand("MADABA", "مأدبا", 2, null, null), CancellationToken.None);
        // The same Arabic word with a fatha and a tatweel is the same name.
        var arabic = await Handlers().Handle(new CreateCityCommand("Madaba Town", "مَادبـا", 2, null, null), CancellationToken.None);

        Assert.Equal(PlatformSettingsErrors.LookupNameTaken, english.Error);
        Assert.Equal(PlatformSettingsErrors.LookupNameTaken, arabic.Error);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Two administrators adding the same city at once both pass the check; the index refuses the second, and that
    /// administrator is told what they would have been told a moment later — not a generic conflict.
    /// </summary>
    [Fact]
    public async Task Losing_the_race_to_the_offered_name_index_answers_name_taken()
    {
        Offered();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(
            new UniqueConstraintConflictException("refused", "ux_cities_offered_name_en", new InvalidOperationException()));

        var result = await Handlers().Handle(new CreateCityCommand("Madaba", "مادبا", 1, null, null), CancellationToken.None);

        Assert.Equal(PlatformSettingsErrors.LookupNameTaken, result.Error);
    }

    [Fact]
    public async Task Any_other_unique_violation_stays_what_it_was()
    {
        Offered();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(
            new UniqueConstraintConflictException("refused", "pk_cities", new InvalidOperationException()));

        await Assert.ThrowsAsync<UniqueConstraintConflictException>(() =>
            Handlers().Handle(new CreateCityCommand("Madaba", "مادبا", 1, null, null), CancellationToken.None));
    }

    [Theory]
    [InlineData("ux_cities_offered_name_en", true)]
    [InlineData("ux_cities_offered_name_ar", true)]
    [InlineData("ux_car_types_offered_name_en", true)]
    [InlineData("ux_car_types_offered_name_ar", true)]
    [InlineData("ux_payments_provider_capture_reference", false)]
    [InlineData(null, false)]
    public void Only_the_offered_name_indexes_mean_name_taken(string? constraint, bool expected) =>
        Assert.Equal(expected, LookupHandlers.IsOfferedNameIndex(constraint));
}
