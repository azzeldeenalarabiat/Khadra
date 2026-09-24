using System.Globalization;
using System.Net;
using System.Text;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI;

/// <summary>
/// The page a tester drives a sandbox checkout from. Exists only when the sandbox is the provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists at all.</b> A real hosted checkout is a page on the provider's domain where the
/// customer chooses an outcome by typing a card. The sandbox has no provider and takes no card, so
/// the outcome is chosen by a button. Everything downstream of that choice — the signature, the raw
/// body, the webhook, the receipt, the replay index — is the real path, deliberately, because a
/// harness that short-circuits to the handler proves nothing about the part that actually broke.
/// </para>
/// <para>
/// <b>The browser posts the webhook, not the server.</b> The tester's page asks this endpoint for a
/// signed body, then <c>fetch</c>es it at <c>/api/v1/payments/webhooks/SANDBOX</c> on the same
/// origin. So the run exercises the controller, the raw-body read, the header dictionary, the
/// <c>[Consumes]</c> negotiation, the webhook rate-limit policy and the whole status mapping —
/// including the duplicate-delivery path that answers 204 — with no HTTP call from the API to
/// itself, no TLS-to-self, and no need for the process to know its own address twice. The secret
/// never leaves the server: what crosses is an HMAC over one specific body, which is exactly what a
/// real provider emits.
/// </para>
/// <para>
/// <b>Why it cannot exist in Production.</b> It is mapped only when the running provider IS the
/// sandbox, which is a condition the two boot guards have already had to pass — so the group cannot
/// be reachable on a host where either would have fired. The explicit <c>IsProduction</c> throw below
/// is nonetheless there, because this project does not let a security property rest on a chain of
/// reasoning in a comment.
/// </para>
/// <para>
/// <b>No card data.</b> There is no card field, because there is no card. The page shows the amount,
/// the booking reference and the expiry — read from the payment row, never from the request — and
/// four buttons. It is never shown a customer's name or email.
/// </para>
/// </remarks>
internal static class SandboxCheckoutEndpoints
{
    /// <summary>
    /// Maps the console, and only when the sandbox is actually the registered provider.
    /// </summary>
    public static void MapSandboxCheckout(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var provider = app.Services.GetRequiredService<IPaymentProvider>();
        if (!PaymentProviders.IsSandbox(provider.Name)) return;

        // Unreachable: Program.cs refuses to build a Production host on this provider, so we cannot
        // be here. Stated anyway — a guard that depends on another guard still being there is a
        // guard that quietly disappears the day somebody reorders the file.
        if (app.Environment.IsProduction())
            throw new InvalidOperationException(
                "The sandbox checkout console must never be mapped in Production.");

        var group = app.MapGroup(SandboxEvents.ConsolePath)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Public)
            .ExcludeFromDescription();

        group.MapGet("/{reference}", ShowAsync);
        group.MapPost("/{reference}/events", BuildEventAsync);
    }

    /// <summary>
    /// The page. Amount, booking and expiry come from the payment ROW; nothing is taken on trust.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IPaymentRepository"/> like any other caller, so the adapter still
    /// touches no database and there is still no <c>sandbox_sessions</c> table. An unknown reference
    /// answers 404 with no detail: the reference is the capability, and confirming which ones exist
    /// would make guessing worthwhile.
    /// <para>
    /// <b>Where it sends the customer afterwards</b> is <see cref="IPaymentSettings.ReturnUrlFor"/>
    /// for the payment's booking — the very URL <c>OpenCheckout</c> handed the provider in
    /// <see cref="CheckoutRequest.ReturnUrl"/>, and where a real hosted checkout would drop the
    /// customer. Recomputed from the row rather than carried in the checkout link, because this page
    /// is anonymous: a return address read from the query string would make it an open redirect.
    /// </para>
    /// </remarks>
    internal static async Task<IResult> ShowAsync(
        string reference,
        [FromServices] IPaymentRepository payments,
        [FromServices] IPaymentSettings settings,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var payment = await payments.GetByProviderReferenceAsync(
            PaymentProviders.Sandbox, reference, cancellationToken);

        if (payment is null) return Results.NotFound();

        return Results.Content(
            Page(payment, clock.UtcNow, settings.ReturnUrlFor(payment.BookingId)),
            "text/html; charset=utf-8");
    }

    /// <summary>
    /// Signs one event body and hands it back. It does not deliver it — the page does.
    /// </summary>
    /// <remarks>
    /// The amount is whatever the page asked for, prefilled from the row and editable on purpose:
    /// paying the wrong amount is a case the platform has real behaviour for (orphan the money,
    /// record a refund) and it has to be clickable too. Nothing here decides anything about the
    /// booking; the webhook does, after verifying the signature this returns.
    /// </remarks>
    private static async Task<IResult> BuildEventAsync(
        string reference,
        [FromBody] SandboxOutcome outcome,
        [FromServices] IPaymentRepository payments,
        [FromServices] IOptions<PaymentOptions> settings,
        [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var payment = await payments.GetByProviderReferenceAsync(
            PaymentProviders.Sandbox, reference, cancellationToken);

        if (payment is null) return Results.NotFound();

        Money? amount = outcome.Kind is "captured" or "refund_settled"
            ? Money.Create(outcome.Amount ?? payment.Amount.Amount, payment.Amount.CurrencyCode)
            : null;

        // The event id is the tester's, so "deliver this again" is literally sending the same body
        // twice — which is the duplicate-webhook case, with no code behind the button.
        var (body, signature) = SandboxEvents.Build(
            outcome.EventId,
            reference,
            outcome.Kind,
            amount,
            settings.Value.WebhookSecret,
            outcome.FailureCode,
            clock.UtcNow);

        return Results.Ok(new { body, signature, header = SandboxEvents.SignatureHeader });
    }

    /// <param name="EventId">The provider's id for this DELIVERY. Reusing one is a replay, on purpose.</param>
    /// <param name="Kind">captured | failed | refund_settled | refund_failed.</param>
    /// <param name="Amount">Major units. Null means "the amount on the row".</param>
    internal sealed record SandboxOutcome(string EventId, string Kind, decimal? Amount, string? FailureCode);

    /// <summary>
    /// One self-contained page. No framework, no build step, and nothing that outlives the sandbox.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain HTML in a string, which is not how anything else in this platform renders and is right
    /// here: it is deleted with the class the day a real adapter lands, and giving it a place in the
    /// Angular console or the Flutter app would mean maintaining it under their rules — i18n, design
    /// tokens, no-literals — for a page only a developer ever sees.
    /// </para>
    /// <para>
    /// <b>Every outcome button returns the customer to <paramref name="returnUrl"/></b> once the
    /// webhook has ACCEPTED the delivery (any 2xx, including the 204 of a duplicate), the way a hosted
    /// checkout hands the browser back when it is done. Not before: a delivery the webhook refused
    /// stays on screen with its answer, because leaving would hide the one thing a tester needs to
    /// read. And not for "Deliver the last one again", which exists to be pressed repeatedly here.
    /// The pause before leaving is there so the answer can be seen; "Stay on this page" cancels it for
    /// a tester who wants to replay. The address is written into a link, never into the script, so
    /// the only encoding it needs is HTML's.
    /// </para>
    /// </remarks>
    internal static string Page(Payment payment, DateTimeOffset now, Uri returnUrl)
    {
        ArgumentNullException.ThrowIfNull(returnUrl);
        var back = WebUtility.HtmlEncode(returnUrl.AbsoluteUri);
        var amount = payment.Amount.Amount.ToString("0.000", CultureInfo.InvariantCulture);
        var currency = WebUtility.HtmlEncode(payment.Amount.CurrencyCode);
        var expired = now >= payment.ExpiresAt;
        var status = WebUtility.HtmlEncode(payment.Status.Name);
        var expiry = payment.ExpiresAt.ToString("u", CultureInfo.InvariantCulture);
        // The refunds this payment owes, so a tester can see what a refund event would act on. A
        // refund event settles or fails the one the sweep has SENT; a Requested one waits for the
        // sweep's next tick.
        var refunds = payment.Refunds.Count == 0
            ? "none"
            : string.Join("<br>", payment.Refunds.Select(refund => WebUtility.HtmlEncode(
                $"{refund.Reason.Name}: {refund.Amount.Amount.ToString("0.000", CultureInfo.InvariantCulture)} {refund.Amount.CurrencyCode}, {refund.Status.Name}")));

        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Sandbox checkout</title>
            <style>
              body{font:16px/1.5 system-ui,sans-serif;margin:0;padding:24px;background:#12100c;color:#f4f1ea}
              .card{max-width:420px;margin:0 auto}
              .warn{background:#7a3d00;border:2px solid #ffb020;border-radius:10px;padding:12px 14px;margin-bottom:20px;font-weight:700}
              dl{display:grid;grid-template-columns:auto 1fr;gap:6px 16px;margin:0 0 20px}
              dt{opacity:.7}dd{margin:0;text-align:end;font-variant-numeric:tabular-nums}
              input{width:100%;box-sizing:border-box;padding:10px;font:inherit;border-radius:8px;border:1px solid #57503f;background:#1d1a14;color:inherit}
              button{width:100%;padding:12px;margin-top:10px;font:inherit;font-weight:600;border:0;border-radius:8px;cursor:pointer}
              .pay{background:#2e7d32;color:#fff}.decline{background:#8e1f1f;color:#fff}
              .again{background:#3a352a;color:#f4f1ea}
              #out{margin-top:18px;padding:12px;border-radius:8px;background:#1d1a14;white-space:pre-wrap;font:13px ui-monospace,monospace}
              label{display:block;margin-top:14px;opacity:.7;font-size:14px}
            </style></head><body><div class="card">
            <div class="warn">SANDBOX &mdash; no money moves. Nothing here touches a card network.</div>
            <dl>
              <dt>Amount</dt><dd>{{amount}} {{currency}}</dd>
              <dt>Attempt</dt><dd>{{status}}</dd>
              <dt>Session expires</dt><dd>{{expiry}}{{(expired ? " (expired)" : "")}}</dd>
              <dt>Refunds</dt><dd>{{refunds}}</dd>
            </dl>
            <label for="amount">Amount to pay (edit to test a mismatch)</label>
            <input id="amount" type="text" inputmode="decimal" value="{{amount}}">
            <label for="evt">Delivery id (send the same one twice to test a replay)</label>
            <input id="evt" type="text" value="">
            <button class="pay"     onclick="go('captured')">Pay</button>
            <button class="decline" onclick="go('failed','card_declined')">Decline</button>
            <button class="pay"     onclick="go('refund_settled')">Refund settled</button>
            <button class="decline" onclick="go('refund_failed','refund_declined')">Refund failed</button>
            <button class="again"   onclick="send()">Deliver the last one again</button>
            <div id="out">Ready.</div>
            <p id="leaving" hidden>Returning to the booking&hellip; <button class="again" onclick="stay()">Stay on this page</button></p>
            <p><a id="back" href="{{back}}">Back to the booking</a></p>
            </div><script>
            const out = document.getElementById('out');
            const evt = document.getElementById('evt');
            const leaving = document.getElementById('leaving');
            const back = document.getElementById('back').href;
            const freshId = () => 'evt_' + Math.random().toString(16).slice(2) + Date.now().toString(16);
            evt.value = freshId();
            let last = null;
            let pending = null;

            // A hosted checkout hands the browser back when it is done; so does this one.
            function returnToBooking() {
              leaving.hidden = false;
              pending = setTimeout(() => location.replace(back), 1500);
            }

            function stay() {
              clearTimeout(pending);
              leaving.hidden = true;
            }

            async function go(kind, failureCode) {
              const body = {
                eventId: evt.value,
                kind,
                amount: kind === 'captured' ? Number(document.getElementById('amount').value) : null,
                failureCode: failureCode || null
              };
              const signed = await fetch(location.pathname + '/events', {
                method: 'POST',
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify(body)
              });
              if (!signed.ok) { out.textContent = 'Could not sign: ' + signed.status; return; }
              last = await signed.json();
              const accepted = await send();
              // A new action is a new delivery. Replaying is what "Deliver the last one again" is for.
              evt.value = freshId();
              // Only once the webhook has taken it. A refusal stays on screen to be read.
              if (accepted) returnToBooking();
            }

            // The real webhook, on the real route, with the real signature header. Same origin, so
            // the API never calls itself. Answers whether the webhook accepted the delivery.
            async function send() {
              if (!last) { out.textContent = 'Nothing to deliver yet.'; return false; }
              const response = await fetch('/api/v1/payments/webhooks/SANDBOX', {
                method: 'POST',
                headers: { 'content-type': 'application/json', [last.header]: last.signature },
                body: last.body
              });
              const text = await response.text();
              out.textContent = 'Webhook answered ' + response.status
                + (text ? '\n' + text : '\n(no body)')
                + '\n\nDelivered:\n' + last.body;
              return response.ok;
            }
            </script></body></html>
            """;
    }
}
