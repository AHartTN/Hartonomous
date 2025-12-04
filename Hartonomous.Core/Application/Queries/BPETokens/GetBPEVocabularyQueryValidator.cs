using FluentValidation;

namespace Hartonomous.Core.Application.Queries.BPETokens;

public sealed class GetBPEVocabularyQueryValidator : AbstractValidator<GetBPEVocabularyQuery>
{
    public GetBPEVocabularyQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0)
            .WithMessage("Page number must be greater than 0");

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
            .WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(1000)
            .WithMessage("Page size cannot exceed 1000");

        RuleFor(x => x.MinFrequency)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinFrequency.HasValue)
            .WithMessage("Minimum frequency must be non-negative");

        RuleFor(x => x.MergeLevel)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MergeLevel.HasValue)
            .WithMessage("Merge level must be non-negative");
    }
}
