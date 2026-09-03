using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Dealers.GetMyDealer;
using Khadra.Application.Dealers.SubmitDealerProfile;
using Khadra.Application.Dealers.UpdateDeliverySettings;
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
