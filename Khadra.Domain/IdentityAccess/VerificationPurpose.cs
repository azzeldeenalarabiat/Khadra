using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

public sealed class VerificationPurpose : Enumeration
{
    public static readonly VerificationPurpose EmailVerification = new(1, "EmailVerification");
    public static readonly VerificationPurpose PasswordReset = new(2, "PasswordReset");

    private VerificationPurpose(int id, string name) : base(id, name)
    {
    }
}
