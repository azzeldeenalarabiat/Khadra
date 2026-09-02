using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.Disputes;

public sealed class DisputeStatus : Enumeration
{
    public static readonly DisputeStatus Open = new(1, "Open");
    public static readonly DisputeStatus UnderReview = new(2, "UnderReview");
    public static readonly DisputeStatus Resolved = new(3, "Resolved");
    public static readonly DisputeStatus Withdrawn = new(4, "Withdrawn");

    private DisputeStatus(int id, string name) : base(id, name)
    {
    }

    public bool IsLive => this == Open || this == UnderReview;
}

// A statement from either party, so the whole exchange sits on one ticket rather than two competing
// tickets about the same booking.
public sealed class DisputeStatement : Entity
{
    private readonly List<string> _evidenceStorageKeys = [];

    public Id TicketId { get; private set; }
    public BookingParty Party { get; private set; } = null!;
    public Id AuthorUserId { get; private set; }
    public string Body { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<string> EvidenceStorageKeys => _evidenceStorageKeys.AsReadOnly();

    private DisputeStatement()
    {
    }

    private DisputeStatement(Id id) : base(id)
    {
    }

    internal static DisputeStatement Create(
        Id ticketId,
        BookingParty party,
        Id authorUserId,
        string body,
        IEnumerable<string>? evidenceStorageKeys,
        DateTimeOffset now)
    {
        var statement = new DisputeStatement(Id.New())
        {
            TicketId = ticketId,
            Party = party,
            AuthorUserId = authorUserId,
            Body = body.Trim(),
            CreatedAt = now
        };

        foreach (var key in evidenceStorageKeys ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key))
                statement._evidenceStorageKeys.Add(key.Trim());
        }

        return statement;
    }
}

// Spec 3.3: disputes are tickets attached to a booking, never free-form messages, so every decision
// is auditable. A ticket is also the ONLY thing that lets a penalty be charged: with no ticket the
// platform applies nothing and the parties settle it between themselves.
public sealed class DisputeTicket : AggregateRoot
{
    private readonly List<DisputeStatement> _statements = [];

    public Id BookingId { get; private set; }
    public Id OpenedByUserId { get; private set; }
    public BookingParty OpenedByParty { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public DisputeStatus Status { get; private set; } = null!;
    public DateTimeOffset OpenedAt { get; private set; }
    // Spec 3.3: the Admin has the same 48 hours as the dealer-approval SLA.
    public DateTimeOffset SlaDeadline { get; private set; }
    public Id? AssignedAdminId { get; private set; }
    public DisputeResolution? Resolution { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyCollection<DisputeStatement> Statements =>
        _statements.OrderBy(statement => statement.CreatedAt).ToList();

    private DisputeTicket()
    {
    }

    private DisputeTicket(Id id) : base(id)
    {
    }

    public static Result<DisputeTicket, Error> Open(
        Id bookingId,
        Id openedByUserId,
        BookingParty openedByParty,
        string? reason,
        TimeSpan sla,
        DateTimeOffset now,
        IEnumerable<string>? evidenceStorageKeys = null)
    {
        ArgumentNullException.ThrowIfNull(openedByParty);
        if (bookingId.IsEmpty || openedByUserId.IsEmpty)
            throw new DomainException("A ticket requires a booking and an author.");
        if (string.IsNullOrWhiteSpace(reason))
            return DisputeErrors.ReasonRequired;

        var ticket = new DisputeTicket(Id.New())
        {
            BookingId = bookingId,
            OpenedByUserId = openedByUserId,
            OpenedByParty = openedByParty,
            Reason = reason.Trim(),
            Status = DisputeStatus.Open,
            OpenedAt = now,
            SlaDeadline = now.Add(sla)
        };
        ticket._statements.Add(DisputeStatement.Create(
            ticket.Id, openedByParty, openedByUserId, reason.Trim(), evidenceStorageKeys, now));
        return ticket;
    }

    // Either party may add to a live ticket, including the party who did not open it. That is why
    // there is one ticket per booking rather than one per complainant.
    public UnitResult<Error> AddStatement(
        BookingParty party,
        Id authorUserId,
        string? body,
        DateTimeOffset now,
        IEnumerable<string>? evidenceStorageKeys = null)
    {
        ArgumentNullException.ThrowIfNull(party);
        if (!Status.IsLive)
            return UnitResult.Failure(DisputeErrors.NotOpen);
        if (string.IsNullOrWhiteSpace(body))
            return UnitResult.Failure(DisputeErrors.StatementRequired);

        _statements.Add(DisputeStatement.Create(Id, party, authorUserId, body, evidenceStorageKeys, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> AssignToAdmin(Id adminUserId)
    {
        if (!Status.IsLive)
            return UnitResult.Failure(DisputeErrors.NotOpen);

        AssignedAdminId = adminUserId;
        Status = DisputeStatus.UnderReview;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Resolve(DisputeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (Status == DisputeStatus.Resolved)
            return UnitResult.Failure(DisputeErrors.AlreadyResolved);
        if (Status == DisputeStatus.Withdrawn)
            return UnitResult.Failure(DisputeErrors.AlreadyWithdrawn);

        Resolution = resolution;
        Status = DisputeStatus.Resolved;
        ClosedAt = resolution.ResolvedAt;
        return UnitResult.Success<Error>();
    }

    // Withdrawal is the amicable path: the parties sorted it out, so nothing is charged.
    public UnitResult<Error> Withdraw(Id byUserId, DateTimeOffset now)
    {
        if (!Status.IsLive)
            return UnitResult.Failure(DisputeErrors.NotOpen);
        if (byUserId != OpenedByUserId)
            return UnitResult.Failure(DisputeErrors.OnlyOpenerCanWithdraw);

        Status = DisputeStatus.Withdrawn;
        ClosedAt = now;
        return UnitResult.Success<Error>();
    }

    public bool IsBreachingSla(DateTimeOffset now) => Status.IsLive && now >= SlaDeadline;
}
