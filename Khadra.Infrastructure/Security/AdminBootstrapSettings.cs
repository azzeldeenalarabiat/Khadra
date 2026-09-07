using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

internal sealed class AdminBootstrapSettings(IOptions<AdminBootstrapOptions> options) : IAdminBootstrapSettings
{
    private readonly AdminBootstrapOptions _options = options.Value;

    public string? Email => _options.Email;

    public string? Phone => _options.Phone;

    public string? FullName => _options.FullName;
}
