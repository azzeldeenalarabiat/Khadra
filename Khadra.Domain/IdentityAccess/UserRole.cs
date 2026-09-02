using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

public sealed class UserRole : Enumeration
{
    public static readonly UserRole Admin = new(1, "Admin");
    public static readonly UserRole DealerOwner = new(2, "DealerOwner");
    public static readonly UserRole DealerEmployee = new(3, "DealerEmployee");
    public static readonly UserRole Customer = new(4, "Customer");

    private UserRole(int id, string name) : base(id, name)
    {
    }

    public bool IsDealerStaff => this == DealerOwner || this == DealerEmployee;

    public bool IsPlatformAdmin => this == Admin;
}
