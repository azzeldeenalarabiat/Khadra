using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

public sealed class UserStatus : Enumeration
{
    public static readonly UserStatus Active = new(1, "Active");
    public static readonly UserStatus Suspended = new(2, "Suspended");

    private UserStatus(int id, string name) : base(id, name)
    {
    }
}
