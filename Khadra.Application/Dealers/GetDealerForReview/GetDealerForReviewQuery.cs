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
/// </summary>
public sealed record DealerDocumentLinkDto(string Type, string Url, DateTimeOffset ExpiresAt);

/// <summary>Everything the review screen needs to make the spec 3.1 decision.</summary>
public sealed record DealerReviewDto(
    DealerProfileDto Dealer,
    string? Description,
    double Latitude,
    double Longitude,
    IReadOnlyList<DealerDocumentLinkDto> Documents,
    IReadOnlyList<DealerReviewTimelineEntry> Timeline,
    bool IsBreachingSla,
    int EmployeeCount);

/// <summary>The application's own history, so an Admin can see what has already happened to it.</summary>
public sealed record DealerReviewTimelineEntry(string Label, string Detail, DateTimeOffset OccurredAt, bool IsComplete);

/// <summary>
/// Admin-only. Mints a fresh signed link per document on every load rather than storing one, so a
/// link cannot outlive the session that legitimately produced it.
/// </summary>
public sealed record GetDealerForReviewQuery(Id DealerId) : IQuery<Result<DealerReviewDto, Error>>;

public sealed class GetDealerForReviewHandler(
    IDealerRepository dealers,
    IDocumentLinkSigner signer,
    IBusinessRulesProvider businessRules,
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
        var rules = await businessRules.GetAsync(cancellationToken);

        var documents = dealer.Documents
            .OrderBy(document => document.Type.Id)
            .Select(document =>
            {
                var link = signer.Sign(document.StorageKey, now);
                return new DealerDocumentLinkDto(document.Type.Name, link.Url, link.ExpiresAt);
            })
            .ToList();

        return new DealerReviewDto(
            DealerProfileDto.From(dealer),
            dealer.Description,
            dealer.Location.Latitude,
            dealer.Location.Longitude,
            documents,
            BuildTimeline(dealer),
            dealer.IsBreachingReviewSla(now),
            dealer.Employees.Count);
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

        if (dealer.ReviewedAt is { } reviewedAt)
        {
            timeline.Add(new DealerReviewTimelineEntry(
                dealer.VerificationStatus.Name,
                dealer.ReviewNote ?? "No note recorded.",
                reviewedAt,
                true));
        }
        else
        {
            timeline.Add(new DealerReviewTimelineEntry(
                "Awaiting admin decision",
                $"Due {dealer.ReviewDueAt:yyyy-MM-dd HH:mm} UTC",
                dealer.ReviewDueAt,
                false));
        }

        return timeline;
    }
}
