using FluentValidation;

namespace Khadra.Application.Legal;

/// <summary>The shape of an acceptance a registration or an invitation carries (Wave 4, W4-8).</summary>
public sealed class ConsentInputValidator : AbstractValidator<ConsentInput>
{
    /// <summary>A guard, not a business figure: two texts are published today, and each is accepted once.</summary>
    public const int MaxVersions = 10;

    public ConsentInputValidator()
    {
        RuleFor(input => input.VersionIds)
            .Must(ids => ids is null || ids.Count <= MaxVersions)
            .WithMessage($"Accept at most {MaxVersions} texts at once.");
        // The version holds both languages; the record must say which one the person read.
        RuleFor(input => input.Language)
            .Must(ConsentInput.IsLanguage)
            .When(input => input.VersionIds is { Count: > 0 })
            .WithMessage("Language must be ar or en.");
    }
}
