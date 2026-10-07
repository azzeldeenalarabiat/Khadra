using Khadra.Application.Common;
using Khadra.Bff.Security;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// What a browser sends that neither BFF forwards to the API, on any route (Wave 4, W4-8): its credentials, and the
/// customer app's version header — the API spares a declared app build from the legal-consent gate, and nothing behind a
/// BFF is the app.
/// </summary>
public sealed class BffProxyRequestHeadersTests
{
    [Fact]
    public void The_browsers_credentials_and_the_apps_version_header_are_never_forwarded()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.khadra.test/api/v1/bookings");
        request.Headers.Add("Cookie", "session=browser");
        request.Headers.Add("Authorization", "Bearer typed-by-the-browser");
        request.Headers.Add("X-XSRF-TOKEN", "antiforgery");
        request.Headers.Add(MobileAppContract.VersionHeader, "1.3.0");
        request.Headers.Add("Accept-Language", "ar");

        ProxyRequestHeaders.RemoveBrowserSupplied(request.Headers);

        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.False(request.Headers.Contains("X-XSRF-TOKEN"));
        Assert.False(request.Headers.Contains(MobileAppContract.VersionHeader));
        // Everything else travels as it came.
        Assert.True(request.Headers.Contains("Accept-Language"));
    }

    /// <summary>The BFF does not reference the application, so the name is restated there; it must stay the API's.</summary>
    [Fact]
    public void The_bff_names_the_app_header_exactly_as_the_api_does()
    {
        Assert.Equal(MobileAppContract.VersionHeader, BffConstants.CustomerAppVersionHeaderName);
        Assert.Contains(MobileAppContract.VersionHeader, ProxyRequestHeaders.NeverForwarded);
    }
}
