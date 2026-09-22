using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Shortlist.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Shortlist;
using Khadra.Domain.Shortlist.Repositories;
using MediatR;

namespace Khadra.Application.Shortlist;

/// <summary>Saves a car to the caller's shortlist.</summary>
/// <remarks>
/// Idempotent: saving what is already saved succeeds and changes nothing. A heart is a toggle on a
/// mobile network, and a retried tap must not be an error the customer has to interpret.
/// </remarks>
public sealed record SaveVehicleCommand(Id CustomerId, Id VehicleId) : ICommand<UnitResult<Error>>;

/// <summary>Removes a car from the caller's shortlist.</summary>
/// <remarks>
/// Idempotent for the same reason, and it deliberately does NOT check that the car is still
/// visible: a customer must be able to clear an entry for a listing that has since been withdrawn,
/// which is precisely the entry they most want gone.
/// </remarks>
public sealed record ForgetVehicleCommand(Id CustomerId, Id VehicleId) : ICommand<UnitResult<Error>>;

/// <summary>The caller's saved cars, newest save first.</summary>
public sealed record ListMyShortlistQuery(Id CustomerId)
    : IQuery<Result<IReadOnlyList<SavedVehicle>, Error>>;

/// <summary>
/// Which of these cars the caller has saved.
/// </summary>
/// <remarks>
/// For the catalogue screens, so a heart can be drawn without loading the list. It answers only
/// about ids the caller NAMED, which is what stops it being a way to read a shortlist through a
/// screen that was never shown one.
/// </remarks>
public sealed record WhichAreSavedQuery(Id CustomerId, IReadOnlyCollection<Id> VehicleIds)
    : IQuery<Result<IReadOnlySet<Id>, Error>>;

public sealed class ShortlistHandlers(
    IShortlistRepository shortlists,
    ICatalogueReader catalogue,
    IShortlistReader reader,
    IBusinessRulesProvider rules,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<SaveVehicleCommand, UnitResult<Error>>,
    IRequestHandler<ForgetVehicleCommand, UnitResult<Error>>,
    IRequestHandler<ListMyShortlistQuery, Result<IReadOnlyList<SavedVehicle>, Error>>,
    IRequestHandler<WhichAreSavedQuery, Result<IReadOnlySet<Id>, Error>>
{
    public async Task<UnitResult<Error>> Handle(
        SaveVehicleCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // THE CATALOGUE'S OWN PREDICATE, not a second one written here.
        //
        // Saving is otherwise the cheapest enumeration oracle on this platform: "saved" on an id the
        // public catalogue answers 404 to would confirm that id exists, and anybody with an account
        // could walk a competitor's unpublished inventory a request at a time. `GetAsync` answers
        // null to a draft, a hidden car, one in maintenance, a suspended gallery's and a genuinely
        // unknown id alike, and this refuses all of them with one code.
        //
        // No availability window is passed: a shortlist has no dates, and asking would only compute
        // an `IsAvailable` nothing reads.
        // The language is immaterial here: this call only asks whether the car may be saved at all,
        // and nothing of the office's writing is read from the answer.
        var vehicle = await catalogue.GetAsync(request.VehicleId, null, Language.Default, cancellationToken);
        if (vehicle is null)
            return ShortlistErrors.VehicleNotAvailable;

        var now = clock.UtcNow;
        var shortlist = await shortlists.GetAsync(request.CustomerId, cancellationToken);

        if (shortlist is null)
        {
            // Created on the first save, never at registration: a row per account for a feature most
            // of them will not use is a table that grows with the user base and says nothing.
            shortlist = CustomerShortlist.Start(request.CustomerId, now);
            shortlists.Add(shortlist);
        }

        var current = await rules.GetAsync(cancellationToken);
        var added = shortlist.Add(request.VehicleId, current.MaxShortlistEntries, now);
        if (added.IsFailure)
            return added.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> Handle(
        ForgetVehicleCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shortlist = await shortlists.GetAsync(request.CustomerId, cancellationToken);
        // Nothing saved at all is not a failure: the customer's list already says what they asked
        // it to say.
        if (shortlist is null)
            return UnitResult.Success<Error>();

        shortlist.Remove(request.VehicleId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<Result<IReadOnlyList<SavedVehicle>, Error>> Handle(
        ListMyShortlistQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Result.Success<IReadOnlyList<SavedVehicle>, Error>(
            await reader.ListAsync(request.CustomerId, cancellationToken));
    }

    public async Task<Result<IReadOnlySet<Id>, Error>> Handle(
        WhichAreSavedQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.VehicleIds.Count == 0)
            return Result.Success<IReadOnlySet<Id>, Error>(new HashSet<Id>());

        return Result.Success<IReadOnlySet<Id>, Error>(
            await shortlists.SavedAmongAsync(request.CustomerId, request.VehicleIds, cancellationToken));
    }
}
