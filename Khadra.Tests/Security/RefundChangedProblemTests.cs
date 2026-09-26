using System.Text.Json;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.WebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.Tests.Security;

/// <summary>
/// The wire shape of <c>booking.refund_changed</c> (owner, 2026-09-26): a 409 whose ProblemDetails
/// carries the CURRENT refund top-level, beside <c>code</c>, so a client can show it and ask again.
/// </summary>
/// <remarks>
/// Asserted on the serialised JSON, because that is what a phone and the website parse: an
/// extension that lived under <c>errors</c>, or a figure serialised as a string, would compile and
/// still leave every client unable to read it.
/// </remarks>
public sealed class RefundChangedProblemTests
{
    private sealed class Probe : ApiControllerBase
    {
        public Probe() => ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        public ObjectResult Answer(Error error) => Failure(error);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonElement Serialised(Error error)
    {
        var result = new Probe().Answer(error);
        var json = JsonSerializer.Serialize(result.Value, Web);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void A_changed_refund_answers_409_with_the_current_figure_beside_the_code()
    {
        var result = new Probe().Answer(BookingErrors.RefundChanged(Money.Jod(76.5m)));
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);

        var body = Serialised(BookingErrors.RefundChanged(Money.Jod(76.5m)));

        Assert.Equal("booking.refund_changed", body.GetProperty("code").GetString());
        var current = body.GetProperty("currentRefund");
        Assert.Equal(JsonValueKind.Number, current.GetProperty("amount").ValueKind);
        Assert.Equal(76.5m, current.GetProperty("amount").GetDecimal());
        Assert.Equal("JOD", current.GetProperty("currency").GetString());
        Assert.False(body.TryGetProperty("errors", out _));
    }

    /// <summary>A payload can never overwrite the names every client reads first.</summary>
    [Fact]
    public void A_payload_cannot_overwrite_the_code_or_the_trace()
    {
        var hostile = Error.Conflict("real.code", "Refused.") with
        {
            Extensions = new Dictionary<string, object?> { ["code"] = "fake.code", ["traceId"] = "fake", ["extra"] = 1 },
        };

        var body = Serialised(hostile);

        Assert.Equal("real.code", body.GetProperty("code").GetString());
        Assert.NotEqual("fake", body.GetProperty("traceId").GetString());
        Assert.Equal(1, body.GetProperty("extra").GetInt32());
    }
}
