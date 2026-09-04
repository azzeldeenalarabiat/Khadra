using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

/// <summary>The access-token lifetime, from the same options the issuer stamps tokens with.</summary>
internal sealed class AccessTokenSettings(IOptions<JwtOptions> options) : IAccessTokenSettings
{
    public int AccessTokenMinutes => options.Value.AccessTokenMinutes;
}
