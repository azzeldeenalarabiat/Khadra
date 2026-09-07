using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using MediatR;

namespace Khadra.Application.Dealers.GetDealerForReview;

/// <summary>
/// One dealer document as the reviewing Admin sees it: what it is, and a short-lived link to look at
/// it. Spec 7 puts these documents in the same class as customer identity papers -- private storage,
/// visible only to Admin, reached through a signed URL that expires.
///
/// <para><see cref="ContentType"/> is what the download endpoint will actually serve this file as,
/// derived from the storage key by the same helper that endpoint uses, so the tile and the response
/// can never disagree. It is sent because the console has no honest way to work it out: the tile
/// rendered "{type}.jpg" for every document, which told an Admin opening a PDF commercial
/// registration that they were about to look at a photograph. The stored file's own name is not
/// sent, and is not worth sending -- a real upload is keyed by a generated guid, so the "name"
/// would be 32 hex characters telling the reviewer nothing the type label does not.</para>
/// </summary>
public sealed record DealerDocumentLinkDto(string Type, string ContentType, string Url, DateTimeOffset ExpiresAt);

/// <summary>Everything the review screen needs to make the spec 3.1 decision.</summary>
public sealed record DealerReviewDto(
    DealerProfileDto Dealer,
    string? Description,
    double Latitude,
    double Longitude,
    IReadOnlyList<DealerDocumentLinkDto> Documents,
    IReadOnlyList<DealerReviewTimelineEntry> Timeline,
    bool IsBreachingSla);
// EmployeeCount is deliberately NOT here. It was, counting every employee row including deactivated
// ones, while Dealer.EmployeeCount on the same response counts only active staff -- so this screen
// and the dealer's own profile showed two different staff numbers for one dealership. One field, one
// meaning: read Dealer.EmployeeCount.

/// <summary>The application's own history, so an Admin can see what has already happened to it.</summary>
public sealed record DealerReviewTimelineEntry(string Label, string Detail, DateTimeOffset OccurredAt, bool IsComplete);

/// <summary>
/// Admin-only. Mints a fresh signed link per document on every load rather than storing one, so a
/// link cannot outlive the session that legitimately produced it.
/// </summary>
public sealed record GetDealerForReviewQuery(Id DealerId) : IQuery<Result<DealerReviewDto, Error>>;

// No IBusinessRulesProvider here on purpose. The current review SLA says nothing about an
// application already in flight: Dealer.Register freezes ReviewDueAt at submission so that changing
// the setting can never re-judge an open application, and IsBreachingReviewSla measures against that
// frozen deadline. The window this application was promised is (ReviewDueAt - SubmittedAt), both of
// which are already on the wire; the current setting would contradict them.
public sealed class GetDealerForReviewHandler(
    IDealerRepository dealers,
    IDocumentLinkSigner signer,
    IClock clock)
    : IRequestHandler<GetDealerForReviewQuery, Result<DealerReviewDto, Error>>
{
    public async Task<Result<DealerReviewDto, Error>> Handle(
        GetDealerForReviewQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dealer = await dealers.GetByIdAsync(request.DealerId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        var now = clock.UtcNow;

        var documents = dealer.Documents
            .OrderBy(document => document.Type.Id)
            .Select(document =>
            {
                var link = signer.Sign(document.StorageKey, now);
                return new DealerDocumentLinkDto(
                    document.Type.Name,
                    DocumentContentTypes.ForStorageKey(document.StorageKey),
                    link.Url,
                    link.ExpiresAt);
            })
            .ToList();

        return new DealerReviewDto(
            DealerProfileDto.From(dealer),
            dealer.Description,
            dealer.Location.Latitude,
            dealer.Location.Longitude,
            documents,
            BuildTimeline(dealer),
            dealer.IsBreachingReviewSla(now));
    }

    /// <summary>
    /// The application's history from what the aggregate already records. Deliberately derived rather
    /// than stored: a dealer has one submission and at most one review decision, so a separate event
    /// log here would be a second source of truth to keep in step for no gain.
    /// </summary>
    private static List<DealerReviewTimelineEntry> BuildTimeline(Dealer dealer)
    {
        var timeline = new List<DealerReviewTimelineEntry>
        {
            new("Application submitted", dealer.BusinessName.Value, dealer.SubmittedAt, true),
            new(
                "Documents attached",
                $"{dealer.Documents.Count} of {DealerDocumentType.Required.Count} required",
                dealer.Documents.Count > 0
                    ? dealer.Documents.Max(document => document.UploadedAt)
                    : dealer.SubmittedAt,
                dealer.HasAllRequiredDocuments)
        };

        // A decision that was made stays on the record even after the dealer answers it: Resubmit
        // restarts the clock but keeps ReviewedAt, because the clarification really did happen.
        if (dealer.ReviewedAt is { } reviewedAt)
        {
            timeline.Add(new DealerReviewTimelineEntry(
                DecisionLabel(dealer, reviewedAt),
                dealer.ReviewNote ?? "No note recorded.",
                reviewedAt,
                true));
        }

        // Then the resubmission that answered it, when there was one -- otherwise the trail jumps
        // from "clarification requested" straight to a deadline nothing explains.
        if (dealer.ReviewedAt is { } previous && dealer.SubmittedAt > previous)
        {
            timeline.Add(new DealerReviewTimelineEntry(
                "Resubmitted by the dealer",
                "The application was corrected and sent back for review.",
                dealer.SubmittedAt,
                true));
        }

        // Whether an Admin still owes a decision is the STATUS's answer, not "has this ever been
        // reviewed". Branching on ReviewedAt alone meant a resubmitted application -- the one whose
        // deadline had just been reset -- showed a completed decision and no pending step at all.
        //
        // No rendered date in the detail: OccurredAt IS the deadline, and the console prints it in
        // the reader's own zone. Spelling it again here as UTC put one moment on the screen twice,
        // in two zones, three words apart.
        if (dealer.VerificationStatus.IsAwaitingAdmin)
        {
            timeline.Add(new DealerReviewTimelineEntry(
                "Awaiting admin decision",
                "No decision has been recorded yet.",
                dealer.ReviewDueAt,
                false));
        }

        return timeline;
    }

    /// <summary>
    /// What the Admin actually did, in words.
    ///
    /// The status is the CURRENT one, so it cannot name a past decision once the dealer has answered
    /// it: a resubmitted application is PendingReview again, and labelling the old step with that
    /// read as though the decision were still pending. When the status can no longer describe the
    /// step, the step says only that a decision was recorded, and its note says which.
    /// </summary>
    private static string DecisionLabel(Dealer dealer, DateTimeOffset reviewedAt) =>
        dealer.SubmittedAt > reviewedAt
            ? "Clarification requested"
            : dealer.VerificationStatus.Name switch
            {
                nameof(DealerVerificationStatus.Approved) => "Approved",
                nameof(DealerVerificationStatus.Rejected) => "Rejected",
                nameof(DealerVerificationStatus.ClarificationNeeded) => "Clarification requested",
                _ => "Decision recorded"
            };
}
