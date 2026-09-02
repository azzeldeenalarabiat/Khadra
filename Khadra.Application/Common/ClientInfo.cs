namespace Khadra.Application.Common;

// Facts about the calling client captured by the API layer, so handlers never touch HttpContext.
public sealed record ClientInfo(string? IpAddress, string? UserAgent)
{
    public static readonly ClientInfo Unknown = new(null, null);
}
