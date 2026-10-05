namespace Khadra.Application.Common.Ports;

/// <summary>
/// Where the customer website is published (<c>App:CustomerAppBaseUrl</c>), for the links the API hands out to pages
/// on it: today the legal pages on <c>/app-config</c> (Wave 2 G1).
/// </summary>
/// <remarks>
/// Null while the setting is empty. No link is invented then: a client offers none, and the API says at startup that
/// legal page links are not being published (pre-launch item 91).
/// </remarks>
public interface ICustomerSiteSettings
{
    Uri? BaseUrl { get; }
}
