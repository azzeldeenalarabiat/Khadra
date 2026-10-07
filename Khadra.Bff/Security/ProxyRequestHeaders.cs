using System.Net.Http.Headers;

namespace Khadra.Bff.Security;

/// <summary>
/// What a browser sends that this BFF never forwards to the API, on any route.
/// </summary>
/// <remarks>
/// The browser never chooses the API identity: its cookie, any bearer it typed and its antiforgery header are removed,
/// and the server-held token is attached afterwards. Nor does it get to be the customer app (Wave 4, W4-8): the API
/// spares a build that declared its version from the legal-consent gate, and the website and the console are never one.
/// One list, so a test can hold it.
/// </remarks>
internal static class ProxyRequestHeaders
{
    public static readonly IReadOnlyList<string> NeverForwarded =
    [
        "Cookie",
        "Authorization",
        BffConstants.XsrfHeaderName,
        BffConstants.CustomerAppVersionHeaderName,
    ];

    public static void RemoveBrowserSupplied(HttpRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        foreach (var name in NeverForwarded)
            headers.Remove(name);
    }
}
