using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Dealers.GetMyDealer;
using Khadra.Application.Dealers.ReviewDealer;
using Khadra.Application.Dealers.SubmitDealerProfile;
using Khadra.Application.Dealers.UpdateDeliverySettings;
using Khadra.Application.Dealers.UpdateProfile;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Step two of spec 3.1 and the dealer's own view of its application.
///
/// The owner id always comes from the token, never from the request, so nobody can file a business
/// against someone else's account.
/// </summary>
[Route("api/v1/dealers")]
public sealed class DealersController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>
    /// Submits the business for the Admin licence check. The dealer is created PENDING_REVIEW and
    /// cannot trade until approved.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.DealerOwner)]
    [HttpPost]
    [RequestSizeLimit(32 * 1024 * 1024)]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Submit(
        [FromForm] SubmitDealerForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var uploads = new List<DealerDocumentUpload>();
        var streams = new List<Stream>();
        try
        {
            foreach (var (type, file) in form.Files())
            {
                if (file is null || file.Length == 0)
                    continue;
                var stream = file.OpenReadStream();
                streams.Add(stream);
                uploads.Add(new DealerDocumentUpload(type, file.FileName, file.ContentType, file.Length, stream));
            }

            var result = await Mediator.Send(
                new SubmitDealerProfileCommand(
                    actor.UserId!.Value,
                    form.BusinessName,
                    form.CommercialRegistrationNumber,
                    form.Latitude,
                    form.Longitude,
                    TimeOnly.Parse(form.OpensAt, System.Globalization.CultureInfo.InvariantCulture),
                    TimeOnly.Parse(form.ClosesAt, System.Globalization.CultureInfo.InvariantCulture),
                    form.Description,
                    form.CityId is null ? null : Id.From(form.CityId.Value),
                    uploads),
                cancellationToken);

            return FromResult(result, created => Created($"/api/v1/dealers/{created.DealerId}", created));
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// The caller's own dealer, readable while PENDING_REVIEW so an applicant can see where they
    /// stand and read any clarification note.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.DealerStaff)]
    [HttpGet("me")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Mine(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetMyDealerQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Spec 3.1 from the dealer side: after a clarification request or a rejection the owner fixes
    /// the problem and puts the application back in the queue, restarting the 48-hour SLA clock.
    ///
    /// Not gated on approval, obviously: this is precisely what an unapproved dealer needs to do.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.DealerOwner)]
    [HttpPost("me/resubmit")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Resubmit(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ResubmitDealerCommand(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// <summary>
    /// The delivery page. Readable by every member of staff (they answer customers' questions about
    /// it); changing it stays owner-only through the PUT below.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.DealerStaff)]
    [HttpGet("me/delivery")]
    [ProducesResponseType<DeliverySettingsViewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetDelivery(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetMyDeliverySettingsQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// A dealer-only action, and the one the approval gate is demonstrated on: while the dealer is
    /// PENDING_REVIEW this returns 403 with <c>dealer.not_approved</c>.
    ///
    /// Guarded twice, deliberately. RequireApprovedDealer stops the request in the pipeline, which is
    /// what a future fleet or booking endpoint will inherit from the attribute alone; the handler
    /// then asks the aggregate again, so the rule still holds for any caller that reaches it another
    /// way. Neither layer is decorative.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPut("me/delivery")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delivery(
        UpdateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new UpdateDeliverySettingsCommand(actor.UserId!.Value, request.IsEnabled, request.RadiusKm),
            cancellationToken);
        return FromResult(result);
    }

    // ── The dealer page (spec 4.1). Owner-only, but not gated on trading: an applicant sent back
    // for clarification fixing their description is exactly who needs these. ──

    public sealed record DayScheduleRequest(
        [Required, MaxLength(9)] string Day,
        bool IsClosed,
        [MaxLength(5)] string? OpensAt,
        [MaxLength(5)] string? ClosesAt);

    public sealed record UpdateProfileRequest(
        [Required, MaxLength(150)] string BusinessName,
        [MaxLength(2000)] string? Description,
        [Range(-90, 90)] double Latitude,
        [Range(-180, 180)] double Longitude,
        [Required] IReadOnlyList<DayScheduleRequest> OperatingHours);

    public sealed record BrandingUploadRequest([Required, MaxLength(100)] string ContentType);

    public sealed record BrandingRequest([Required, MaxLength(500)] string StorageKey);

    [Authorize(Policy = SecurityPolicies.DealerOwner)]
    [HttpPut("me/profile")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new UpdateDealerProfileCommand(
                actor.UserId!.Value,
                request.BusinessName,
                request.Description,
                request.Latitude,
                request.Longitude,
                [.. request.OperatingHours.Select(day => new DayScheduleInput(day.Day, day.IsClosed, day.OpensAt, day.ClosesAt))]),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Step one of a logo or cover upload; step two is PUT /api/v1/uploads/{token}.</summary>
    [Authorize(Policy = SecurityPolicies.DealerOwner)]
    [HttpPost("me/branding/{kind}/upload-url")]
    [ProducesResponseType<BrandingUploadDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> RequestBrandingUpload(string kind, [FromBody] BrandingUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RequestBrandingUploadCommand(actor.UserId!.Value, kind, request.ContentType), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Step three: the uploaded image becomes the logo or the cover.</summary>
    [Authorize(Policy = SecurityPolicies.DealerOwner)]
    [HttpPut("me/branding/{kind}")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> SetBranding(string kind, [FromBody] BrandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new SetBrandingCommand(actor.UserId!.Value, kind, request.StorageKey), cancellationToken);
        return FromResult(result);
    }
}

/// <summary>
/// Serves dealer logos and covers.
///
/// Anonymous and cacheable like car photos: spec 4.1 shows them to every customer. The path is
/// pinned to the dealer-branding scope, which is deliberately NOT the dealers/{id} scope where the
/// licence scans and owner IDs live -- this endpoint can never be talked into serving one of those.
/// </summary>
[Route("api/v1/dealer-images")]
public sealed class DealerImagesController(IDocumentStorage storage) : ApiControllerBase
{
    private static readonly System.Text.RegularExpressions.Regex BrandingFileName =
        new(@"^[0-9a-z-]+\.(jpg|jpeg|png|webp)$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    [AllowAnonymous]
    [HttpGet("dealer-branding/{dealerId:guid}/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid dealerId, string fileName, CancellationToken cancellationToken)
    {
        // Anything that is not a generated branding file name is a 404, not a storage exception: the
        // endpoint is anonymous, and a stray dot or slash must never reach the key parser.
        if (!BrandingFileName.IsMatch(fileName))
            return NotFound();

        var content = await storage.OpenAsync($"dealer-branding/{dealerId}/{fileName}", cancellationToken);
        if (content is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(content, Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        });
    }
}

/// <summary>
/// Multipart form for the dealer application. Operating hours are one open/close window applied to
/// every day: spec 3.1 does not say hours vary by day, and the richer shape belongs with the dealer
/// profile editor rather than the application form.
/// </summary>
public sealed class SubmitDealerForm
{
    [Required, StringLength(150)]
    public string BusinessName { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string CommercialRegistrationNumber { get; init; } = string.Empty;

    [Range(-90, 90)]
    public double Latitude { get; init; }

    [Range(-180, 180)]
    public double Longitude { get; init; }

    [Required]
    public string OpensAt { get; init; } = "08:00";

    [Required]
    public string ClosesAt { get; init; } = "20:00";

    [StringLength(2000)]
    public string? Description { get; init; }

    public Guid? CityId { get; init; }

    // Spec 3.1 requires all three: the commercial registration, proof of green-plate vehicle
    // registration, and the owner's ID.
    public IFormFile? CommercialRegistration { get; init; }

    public IFormFile? VehicleRegistration { get; init; }

    public IFormFile? OwnerIdentity { get; init; }

    public IEnumerable<(string Type, IFormFile? File)> Files()
    {
        yield return ("CommercialRegistration", CommercialRegistration);
        yield return ("VehicleRegistration", VehicleRegistration);
        yield return ("OwnerIdentity", OwnerIdentity);
    }
}

public sealed record UpdateDeliveryRequest(bool IsEnabled, [param: Range(0, 200)] decimal RadiusKm);
