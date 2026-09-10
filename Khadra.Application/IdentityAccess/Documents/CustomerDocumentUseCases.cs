using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.Documents;

/// <summary>
/// Uploads one identity document for the signed-in customer.
///
/// The stream comes from the transport but the handler never sees HttpContext: it is handed content
/// plus what the transport already knows about it. The user id is the ACTOR's, never a value from the
/// request body, so one customer cannot file paperwork against another's account.
/// </summary>
public sealed record UploadCustomerDocumentCommand(
    Id UserId,
    string DocumentType,
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content) : ICommand<Result<CustomerDocumentDto, Error>>;

public sealed class UploadCustomerDocumentCommandValidator : AbstractValidator<UploadCustomerDocumentCommand>
{
    public UploadCustomerDocumentCommandValidator()
    {
        RuleFor(command => command.DocumentType).NotEmpty().MaximumLength(30);
        RuleFor(command => command.FileName).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(100);
        // The real limits are configured and enforced in the handler; this only rejects nonsense.
        RuleFor(command => command.SizeBytes).GreaterThan(0);
    }
}

public sealed class UploadCustomerDocumentHandler(
    IUserRepository users,
    IDocumentStorage storage,
    IDocumentPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UploadCustomerDocumentCommand, Result<CustomerDocumentDto, Error>>
{
    public async Task<Result<CustomerDocumentDto, Error>> Handle(
        UploadCustomerDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var type = ParseType(request.DocumentType);
        if (type is null)
            return IdentityErrors.UnsupportedDocumentType;

        if (request.SizeBytes <= 0 || request.SizeBytes > policy.MaximumSizeBytes)
            return IdentityErrors.DocumentTooLarge;

        if (!policy.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
            return IdentityErrors.InvalidDocumentContent;

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        var stored = await storage.SaveAsync(
            $"customers/{user.Id.Value}",
            request.FileName,
            request.ContentType,
            request.Content,
            cancellationToken);

        var superseded = user.AttachDocument(
            type, stored.StorageKey, stored.ContentType, stored.SizeBytes, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Only after the row is committed: deleting first would lose the old file if the save failed,
        // leaving the customer with a record pointing at nothing.
        if (superseded is not null)
            await storage.DeleteAsync(superseded, cancellationToken);

        var document = user.Documents.Single(candidate => candidate.StorageKey == stored.StorageKey);
        return CustomerDocumentDto.From(document);
    }

    private static CustomerDocumentType? ParseType(string? raw) =>
        Enumeration.GetAll<CustomerDocumentType>()
            .SingleOrDefault(type => string.Equals(type.Name, raw, StringComparison.OrdinalIgnoreCase));
}

public sealed record ListCustomerDocumentsQuery(Id UserId) : IQuery<Result<CustomerDocumentsDto, Error>>;

public sealed class ListCustomerDocumentsHandler(IUserRepository users)
    : IRequestHandler<ListCustomerDocumentsQuery, Result<CustomerDocumentsDto, Error>>
{
    public async Task<Result<CustomerDocumentsDto, Error>> Handle(
        ListCustomerDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        return new CustomerDocumentsDto(
            [.. user.Documents.OrderBy(document => document.Type.Id).Select(CustomerDocumentDto.From)],
            user.HasCompleteRenterDocuments,
            // The aggregate's rule, not this handler's copy of it. The gallery's handover panel asks
            // the same question through a different endpoint, and a rule stated twice is a rule that
            // drifts -- the way it would drift is the customer being told their paperwork is complete
            // while the gallery is told something is missing.
            [.. user.MissingRenterDocumentTypes().Select(type => type.Name)]);
    }
}

/// <summary>
/// Mints a short-lived link to one of the actor's own documents (spec 7).
///
/// Scoped to the actor on purpose: this pass gives a customer access to their own paperwork only. The
/// dealer case -- "visible once that customer sends a booking request to them" -- needs the booking
/// context and belongs with the booking module, not here.
/// </summary>
public sealed record CreateCustomerDocumentLinkQuery(Id UserId, Id DocumentId)
    : IQuery<Result<SignedDocumentLink, Error>>;

public sealed class CreateCustomerDocumentLinkHandler(
    IUserRepository users,
    IDocumentLinkSigner signer,
    IClock clock)
    : IRequestHandler<CreateCustomerDocumentLinkQuery, Result<SignedDocumentLink, Error>>
{
    public async Task<Result<SignedDocumentLink, Error>> Handle(
        CreateCustomerDocumentLinkQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        var document = user.FindDocument(request.DocumentId);
        // A document belonging to someone else is reported as not found, not as forbidden: a 403
        // would confirm the id exists.
        if (document is null)
            return IdentityErrors.DocumentNotFound;

        return signer.Sign(document.StorageKey, clock.UtcNow);
    }
}
