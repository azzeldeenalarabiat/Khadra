using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using MediatR;

namespace Khadra.Application.Disputes.RaiseDispute;

// The parties' side of spec 3.3: either the customer or the dealer opens a ticket from a booking,
// with a reason and optional evidence; either adds to it; the opener may withdraw it. No money is
// touched anywhere in this file -- that is the Admin's decision, and the Admin's alone.

/// <summary>Step one of attaching evidence: a short-lived URL to PUT the bytes to.</summary>
public sealed record RequestDisputeEvidenceUploadCommand(Id UserId, Id BookingId, string FileName, string ContentType)
    : ICommand<Result<EvidenceUploadDto, Error>>;

public sealed record OpenDisputeCommand(Id UserId, Id BookingId, string Reason, IReadOnlyList<string> EvidenceKeys)
    : ICommand<Result<DisputeDto, Error>>;

public sealed record AddDisputeStatementCommand(Id UserId, Id TicketId, string Body, IReadOnlyList<string> EvidenceKeys)
    : ICommand<Result<DisputeDto, Error>>;

public sealed record WithdrawDisputeCommand(Id UserId, Id TicketId) : ICommand<Result<DisputeDto, Error>>;

public sealed record GetMyDisputeQuery(Id UserId, Id TicketId) : IQuery<Result<DisputeDto, Error>>;

public sealed class RequestDisputeEvidenceUploadCommandValidator : AbstractValidator<RequestDisputeEvidenceUploadCommand>
{
    public RequestDisputeEvidenceUploadCommandValidator()
    {
        RuleFor(command => command.FileName).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(100);
    }
}

public sealed class OpenDisputeCommandValidator : AbstractValidator<OpenDisputeCommand>
{
    public OpenDisputeCommandValidator()
    {
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(command => command.EvidenceKeys).NotNull();
        RuleFor(command => command.EvidenceKeys.Count).LessThanOrEqualTo(10);
        RuleForEach(command => command.EvidenceKeys).NotEmpty().MaximumLength(300);
    }
}

public sealed class AddDisputeStatementCommandValidator : AbstractValidator<AddDisputeStatementCommand>
{
    public AddDisputeStatementCommandValidator()
    {
        RuleFor(command => command.Body).NotEmpty().MaximumLength(4000);
        RuleFor(command => command.EvidenceKeys).NotNull();
        RuleFor(command => command.EvidenceKeys.Count).LessThanOrEqualTo(10);
        RuleForEach(command => command.EvidenceKeys).NotEmpty().MaximumLength(300);
    }
}

public sealed class RaiseDisputeHandlers(
    IBookingRepository bookings,
    IDisputeTicketRepository tickets,
    BookingPartyResolver parties,
    DisputeViewComposer composer,
    IUploadTicketService uploads,
    IDocumentStorage storage,
    IDocumentPolicySettings policy,
    IBusinessRulesProvider rules,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<RequestDisputeEvidenceUploadCommand, Result<EvidenceUploadDto, Error>>,
    IRequestHandler<OpenDisputeCommand, Result<DisputeDto, Error>>,
    IRequestHandler<AddDisputeStatementCommand, Result<DisputeDto, Error>>,
    IRequestHandler<WithdrawDisputeCommand, Result<DisputeDto, Error>>,
    IRequestHandler<GetMyDisputeQuery, Result<DisputeDto, Error>>
{
    public async Task<Result<EvidenceUploadDto, Error>> Handle(
        RequestDisputeEvidenceUploadCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Evidence is scoped to the BOOKING, not the ticket: it is uploaded before the ticket exists,
        // and a ticket opened later simply quotes the keys.
        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        var party = await parties.ResolveAsync(booking, request.UserId, cancellationToken);
        if (party.IsFailure)
            return party.Error;

        if (!policy.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
            return DisputeErrors.InvalidEvidenceType;

        var key = $"{EvidencePrefix(booking.Id)}{Guid.NewGuid():N}{Extension(request.ContentType)}";
        var ticket = uploads.Issue(key, request.ContentType, clock.UtcNow);
        return new EvidenceUploadDto(ticket.UploadUrl, ticket.StorageKey, ticket.ExpiresAt);
    }

    public async Task<Result<DisputeDto, Error>> Handle(OpenDisputeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        var party = await parties.ResolveAsync(booking, request.UserId, cancellationToken);
        if (party.IsFailure)
            return party.Error;

        var now = clock.UtcNow;
        // Judged against THIS booking's frozen window, never today's setting.
        if (!booking.CanBeDisputed(now))
            return DisputeErrors.BookingNotDisputable;

        // One ticket per booking; the other party adds to it rather than opening a second one. The
        // partial unique index is the backstop for two opens racing past this check.
        if (await tickets.HasLiveTicketAsync(booking.Id, cancellationToken))
            return DisputeErrors.AlreadyOpen;

        var evidence = await VerifyEvidenceAsync(booking.Id, request.EvidenceKeys, cancellationToken);
        if (evidence.IsFailure)
            return evidence.Error;

        var businessRules = await rules.GetAsync(cancellationToken);
        var ticket = DisputeTicket.Open(
            booking.Id,
            request.UserId,
            party.Value,
            request.Reason,
            TimeSpan.FromHours(businessRules.AdminSlaHours),
            now,
            request.EvidenceKeys);
        if (ticket.IsFailure)
            return ticket.Error;

        await tickets.AddAsync(ticket.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await composer.ComposeAsync(ticket.Value, booking, cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(AddDisputeStatementCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadOwnedAsync(request.TicketId, request.UserId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (ticket, booking, party) = loaded.Value;

        var evidence = await VerifyEvidenceAsync(booking.Id, request.EvidenceKeys, cancellationToken);
        if (evidence.IsFailure)
            return evidence.Error;

        var added = ticket.AddStatement(party, request.UserId, request.Body, clock.UtcNow, request.EvidenceKeys);
        if (added.IsFailure)
            return added.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await composer.ComposeAsync(ticket, booking, cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(WithdrawDisputeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadOwnedAsync(request.TicketId, request.UserId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (ticket, booking, _) = loaded.Value;

        // The aggregate decides whether this person may withdraw (only the opener can).
        var withdrawn = ticket.Withdraw(request.UserId, clock.UtcNow);
        if (withdrawn.IsFailure)
            return withdrawn.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await composer.ComposeAsync(ticket, booking, cancellationToken);
    }

    public async Task<Result<DisputeDto, Error>> Handle(GetMyDisputeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await LoadOwnedAsync(request.TicketId, request.UserId, cancellationToken);
        if (loaded.IsFailure)
            return loaded.Error;
        var (ticket, booking, _) = loaded.Value;

        return await composer.ComposeAsync(ticket, booking, cancellationToken);
    }

    /// <summary>A ticket plus its booking, for someone who is a party to that booking; not_found otherwise.</summary>
    private async Task<Result<(DisputeTicket Ticket, Booking Booking, BookingParty Party), Error>> LoadOwnedAsync(
        Id ticketId,
        Id userId,
        CancellationToken cancellationToken)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, cancellationToken);
        if (ticket is null)
            return DisputeErrors.NotFound;

        var booking = await bookings.GetByIdAsync(ticket.BookingId, cancellationToken);
        if (booking is null)
            return DisputeErrors.BookingMissing;

        var party = await parties.ResolveAsync(booking, userId, cancellationToken);
        // The resolver says booking.not_found; from here the thing being asked for is the dispute.
        if (party.IsFailure)
            return DisputeErrors.NotFound;

        return (ticket, booking, party.Value);
    }

    /// <summary>
    /// Every quoted key must sit under this booking's evidence prefix AND have bytes behind it. Without
    /// both, "I uploaded it" is a client's promise, and a ticket could point at another booking's
    /// files.
    /// </summary>
    private async Task<UnitResult<Error>> VerifyEvidenceAsync(
        Id bookingId,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var prefix = EvidencePrefix(bookingId);
        foreach (var key in keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                return UnitResult.Failure(DisputeErrors.EvidenceOutsideBooking);

            await using var content = await storage.OpenAsync(key, cancellationToken);
            if (content is null)
                return UnitResult.Failure(DisputeErrors.EvidenceNotUploaded);
        }

        return UnitResult.Success<Error>();
    }

    private static string EvidencePrefix(Id bookingId) => $"disputes/{bookingId.Value}/";

    private static string Extension(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => ".jpg"
        };
}
