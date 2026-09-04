using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

public sealed class VerificationPurpose : Enumeration
{
    public static readonly VerificationPurpose EmailVerification = new(1, "EmailVerification");
    public static readonly VerificationPurpose PasswordReset = new(2, "PasswordReset");
    // A dealer owner invited a member of staff (spec 4.2). Its own purpose, not a reused password
    // reset: the lifetime is days rather than minutes, the email says something different, and the
    // Employees screen needs to tell an invitation that is still open from one that was accepted.
    public static readonly VerificationPurpose EmployeeInvitation = new(3, "EmployeeInvitation");

    // One administrator invited another (spec 3). Its own purpose rather than the employee's: the
    // email says something different, there is no dealership to name in it, and an open admin
    // invitation must be tellable from an open staff one when a token is redeemed.
    public static readonly VerificationPurpose AdminInvitation = new(4, "AdminInvitation");

    private VerificationPurpose(int id, string name) : base(id, name)
    {
    }
}
