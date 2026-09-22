using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary>The <c>MobileApp</c> section, already validated at startup, as the port reads it.</summary>
internal sealed class MobileAppPolicySettings(IOptions<MobileAppOptions> options) : IMobileAppPolicySettings
{
    private readonly MobileAppOptions _options = options.Value;

    public AppVersion? MinimumSupportedVersion => _options.ParsedMinimum;

    public Uri? UpdateUrl => _options.ParsedUpdateUrl;
}
