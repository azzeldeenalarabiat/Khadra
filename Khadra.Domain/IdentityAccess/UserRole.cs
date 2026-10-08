using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// ADD-ONLY (pre-launch item 19): rows store the NAME and are read back through Enumeration.FromName, which throws
// for a name that no longer exists. Renaming or removing a member makes every row carrying it unreadable, and audit
// rows cannot be corrected. Add members; never rename or remove one. PersistedEnumerationNamesTests holds the list.
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
