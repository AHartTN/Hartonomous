using FluentValidation;

namespace Hartonomous.Core.Application.Queries.Landmarks;

public sealed class GetLandmarkByNameQueryValidator : AbstractValidator<GetLandmarkByNameQuery>
{
    public GetLandmarkByNameQueryValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Landmark name is required")
            .MaximumLength(256)
            .WithMessage("Landmark name cannot exceed 256 characters");
    }
}
