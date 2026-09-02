using Khadra.Application.Common;

namespace Khadra.Infrastructure.Security;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
