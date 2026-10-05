using Khadra.Application.Common.Ports;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary><c>App:CustomerAppBaseUrl</c>, as an absolute http(s) address, or null while it is empty or not one.</summary>
internal sealed class CustomerSiteSettings(IOptions<AppOptions> options) : ICustomerSiteSettings
{
    public Uri? BaseUrl =>
        Uri.TryCreate(options.Value.CustomerAppBaseUrl?.Trim(), UriKind.Absolute, out var url) &&
        (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;
}
