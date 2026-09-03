using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// Spec 5.1: a minimum age is enforced at registration. The VALUE is a configured business rule and
// arrives from IBusinessRulesProvider, never from a constant here (see CLAUDE.md).
//
// `today` is passed in rather than read from the clock so the rule stays pure and testable, matching
// PasswordPolicy alongside it. A null minimum means the owner has not set one and nobody is refused;
// that is a real state the platform ships in, not a bug.
public static class RenterAgePolicy
{
    public static UnitResult<Error> Validate(DateOnly? dateOfBirth, int? minimumAge, DateOnly today)
    {
        if (minimumAge is null)
            return UnitResult.Success<Error>();

        if (dateOfBirth is null)
            return UnitResult.Failure(IdentityErrors.DateOfBirthRequired);

        if (dateOfBirth.Value > today)
            return UnitResult.Failure(IdentityErrors.InvalidDateOfBirth);

        if (AgeOn(dateOfBirth.Value, today) < minimumAge.Value)
            return UnitResult.Failure(IdentityErrors.UnderMinimumAge(minimumAge.Value));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Completed years. Counting whole birthdays rather than dividing days avoids the leap-year
    /// error that turns a 20-year-old into a 21-year-old for one day every four years.
    /// </summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (today < dateOfBirth.AddYears(age))
            age--;
        return age;
    }
}
