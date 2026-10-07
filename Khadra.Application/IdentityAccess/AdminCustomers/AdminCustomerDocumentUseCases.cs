using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Application.Notifications;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.IdentityAccess.AdminCustomers;

/// <summary>
/// An administrator opens one of a customer's documents (Wave 4, W4-9; checklist 27).
/// </summary>
/// <remarks>
/// Streamed, never a signed link: a link is a bearer credential that outlives the request, and item 14's exposure is
/// not to be widened to passports. The view is recorded BEFORE a byte is sent, as the office's route records it (item
/// 86), and if it cannot be recorded nothing is sent. The endpoint cannot know an administrator's reason for looking,
/// so the record is the control.
/// </remarks>
public sealed record OpenCustomerDocumentCommand(Id CustomerId, Id DocumentId) : ICommand<Result<OpenedDocument, Error>>;

/// <summary>
/// An administrator could not accept one of a customer's documents, and says why (Wave 4, W4-9; owner, D3).
/// </summary>
/// <param name="ExpectedUploadedAt">
/// When the file the administrator opened was uploaded, exactly as the server sent it. A different file — the customer
/// uploaded a new one since — answers 409 <c>documents.changed_since_viewed</c> rather than rejecting a file nobody
/// looked at.
/// </param>
public sealed record RejectCustomerDocumentCommand(Id CustomerId, Id DocumentId, string Reason, DateTimeOffset ExpectedUploadedAt)
    : ICommand<Result<CustomerProfile, Error>>;

public sealed class RejectCustomerDocumentCommandValidator : AbstractValidator<RejectCustomerDocumentCommand>
{
    public RejectCustomerDocumentCommandValidator()
    {
        // 500: it is the width of customer_documents.review_note, and the customer reads it as written.
        RuleFor(command => command.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A reason is required: the customer reads it on their documents page.")
            .Must(reason => reason is null || reason.Trim().Length <= CustomerDocument.MaxReviewNoteLength)
            .WithMessage($"A reason is at most {CustomerDocument.MaxReviewNoteLength} characters: it is the width of review_note.");
        // Required, not optional (the advisor's review): a request that leaves it out must not skip the guard.
        RuleFor(command => command.ExpectedUploadedAt)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("Say which upload you viewed: the document's uploadedAt, as the profile sent it.");
    }
}

public sealed class AdminCustomerDocumentHandlers(
    IUserRepository users,
    IDocumentStorage storage,
    DocumentAccessRecorder accessLog,
    AdminActionRecorder audit,
    DealerTeamNotifier notifier,
    ICustomerAdminReader reader,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<OpenCustomerDocumentCommand, Result<OpenedDocument, Error>>,
      IRequestHandler<RejectCustomerDocumentCommand, Result<CustomerProfile, Error>>
{
    public async Task<Result<OpenedDocument, Error>> Handle(OpenCustomerDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await CustomerAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return IdentityErrors.UserNotFound;

        // Another customer's document id reads as not found, as it does on every other route.
        var document = customer.FindDocument(request.DocumentId);
        if (document is null)
            return IdentityErrors.DocumentNotFound;

        // The row survived and the file did not: nothing to send, so nothing was disclosed and nothing is recorded.
        var content = await storage.OpenAsync(document.StorageKey, cancellationToken);
        if (content is null)
            return IdentityErrors.DocumentNotFound;

        try
        {
            accessLog.RecordAdminView(customer.Id, document.Id, document.Type, document.UploadedAt);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Nothing has been sent yet, so the handle is ours to close.
            await content.DisposeAsync();
            throw;
        }

        return new OpenedDocument(content, DocumentContentTypes.ForStorageKey(document.StorageKey));
    }

    public async Task<Result<CustomerProfile, Error>> Handle(RejectCustomerDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await CustomerAsync(request.CustomerId, cancellationToken);
        if (customer is null)
            return IdentityErrors.UserNotFound;

        var document = customer.FindDocument(request.DocumentId);
        if (document is null)
            return IdentityErrors.DocumentNotFound;

        var before = $"{document.Type.Name}:{document.Status.Name}";
        var rejected = customer.RejectDocument(request.DocumentId, request.Reason, request.ExpectedUploadedAt);
        if (rejected.IsFailure)
            return rejected.Error;

        var now = clock.UtcNow;

        // A reference, never the customer's name: audit_entries is append-only, and identity written into it could
        // never be taken out again. The document is named by its type, the slot it fills.
        audit.Record(
            AuditAction.CustomerDocumentRejected,
            AuditEntityType.Customer,
            customer.Id,
            $"Customer {customer.Id.Value:N}"[..17],
            before,
            $"{document.Type.Name}:{document.Status.Name}",
            document.ReviewNote);

        // No subject and no reference (the advisor's review): it is about the account, not a booking, and installed apps
        // print a reference raw. The words never carry the reason; the documents page does.
        await notifier.NotifyCustomerFromPlatformAsync(customer.Id, NotificationKind.YourDocumentRejected, now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The customer replaced the file between this load and this save: the decision was about a file that is no
            // longer there (the document's own concurrency token; Wave 4, W4-9).
            return IdentityErrors.DocumentChangedSinceViewed;
        }

        var profile = await reader.GetAsync(customer.Id, cancellationToken);
        return profile is null ? IdentityErrors.UserNotFound : profile;
    }

    /// <summary>
    /// The account, if it is a customer's. Not found for anyone else, as the other administrator routes on customers
    /// answer: "wrong role" would confirm the id names a dealer or an administrator.
    /// </summary>
    private async Task<User?> CustomerAsync(Id userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is not null && user.Role == UserRole.Customer ? user : null;
    }
}
