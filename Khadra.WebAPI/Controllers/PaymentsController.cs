using Khadra.Application.Payments.Dtos;
using Khadra.Application.Payments.OpenCheckout;
using Khadra.Application.Payments.ReceiveProviderEvent;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Paying a booking's deposit, and hearing back from the provider about it.
/// </summary>
/// <remarks>
/// <para>
/// Two endpoints with opposite trust models. The first is the customer's, authenticated and scoped
/// to their own booking. The second is anonymous, because a payment provider has no session with
/// this platform — what stands in for authentication there is a signature over the raw body, checked
/// by the adapter before anything is read.
/// </para>
/// <para>
/// This build ships with no provider configured, so both answer honestly rather than pretending:
/// the checkout returns 503 <c>payments.provider_unavailable</c>, and the webhook returns 401,
/// because with no secret there is no way to tell a provider from anyone else who found the URL.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1")]
public sealed class PaymentsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>
    /// Starts, resumes or replaces the customer's checkout for a booking's deposit.
    /// </summary>
    /// <remarks>
    /// Takes no amount. A request that named its own figure would be a request to choose what a
    /// rental costs; the deposit comes from the booking's own frozen pricing.
    ///
    /// Repeating it is safe and is the intended way to recover: a customer who closed the tab, lost
    /// signal, or came back tomorrow gets the same live session back rather than a second one.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpPost("bookings/{bookingId:guid}/deposit-checkout")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> OpenDepositCheckout(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new OpenDepositCheckoutCommand(actor.UserId!.Value, Id.From(bookingId)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// A payment provider reporting what became of a checkout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anonymous by necessity and rate limited on its own policy, because a provider catching up
    /// after an outage delivers a burst and every delivery in it is somebody's money.
    /// </para>
    /// <para>
    /// The body is read as RAW TEXT and passed through unparsed: a signature covers the exact bytes
    /// the provider sent, so model binding it into a DTO and re-serialising would break verification
    /// on every message. Nothing here trusts the body until the adapter has verified it.
    /// </para>
    /// <para>
    /// 200 means "recorded, do not send this again" — including for an event about a reference this
    /// platform never issued, which is recorded and ignored, because there is nothing a retry could
    /// change. Anything that could succeed on a retry answers 5xx so the provider tries again.
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Webhook)]
    [HttpPost("payments/webhooks/{provider}")]
    [Consumes("application/json", "text/plain")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ReceiveProviderEvent(string provider, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        // Every header, because which one carries the signature is the adapter's business, not this
        // controller's. A controller that picked the header would have to change per provider.
        var headers = Request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var result = await Mediator.Send(new ReceiveProviderEventCommand(body, headers), cancellationToken);
        return FromResult(result);
    }
}
