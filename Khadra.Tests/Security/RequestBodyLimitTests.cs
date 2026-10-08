using System.Reflection;
using System.Text.Json;
using Khadra.Application.Dealers.SubmitDealerProfile;
using Khadra.Bff.Security;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.Tests.Security;

/// <summary>
/// The body limits the BFF and the API enforce, kept in step (pre-launch item 33): a request the API would accept must
/// never be refused by the proxy in front of it, and a dealer's submission must carry every required document at the
/// configured size.
/// </summary>
public sealed class RequestBodyLimitTests
{
    private static readonly JsonDocumentOptions Relaxed = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every <c>[RequestSizeLimit]</c> the API declares, on a controller or an action.</summary>
    private static IEnumerable<long> DeclaredApiLimits() =>
        typeof(Khadra.WebAPI.WebApiAssemblyMarker).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => new MemberInfo[] { type }.Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)))
            .SelectMany(member => member.GetCustomAttributes<RequestSizeLimitAttribute>())
            .Select(attribute => ((IRequestSizeLimitMetadata)attribute).MaxRequestBodySize ?? long.MaxValue);

    [Fact]
    public void The_bff_lets_through_every_body_the_api_accepts()
    {
        var limits = DeclaredApiLimits().ToList();

        Assert.NotEmpty(limits);
        Assert.Contains(DealerSubmissionLimits.MaximumRequestBytes, limits);
        Assert.All(limits, limit => Assert.True(
            limit <= BffConstants.MaxRequestBodyBytes,
            $"The API accepts a body of {limit} bytes, and the BFF in front of it refuses anything over " +
            $"{BffConstants.MaxRequestBodyBytes}: raise BffConstants.MaxRequestBodyBytes with it."));
    }

    [Fact]
    public void A_dealer_submission_carries_every_required_document_at_the_configured_size()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.File("Khadra.WebAPI", "appsettings.json")), Relaxed);
        var configured = settings.RootElement.GetProperty("Documents").GetProperty("MaximumSizeBytes").GetInt64();

        Assert.True(DealerSubmissionLimits.Fits(configured));
        // The startup check refuses a size the submission cannot carry, rather than a 413 on every application.
        var tooLarge = (DealerSubmissionLimits.MaximumRequestBytes / DealerDocumentType.Required.Count) + 1;
        Assert.False(DealerSubmissionLimits.Fits(tooLarge));
    }
}
