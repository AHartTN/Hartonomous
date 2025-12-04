using FluentValidation;

namespace Hartonomous.Core.Application.Queries.Constants;

public sealed class GetNearbyConstantsQueryValidator : AbstractValidator<GetNearbyConstantsQuery>
{
    public GetNearbyConstantsQueryValidator()
    {
        RuleFor(x => x.X)
            .InclusiveBetween(-1.0, 1.0)
            .WithMessage("X coordinate must be between -1.0 and 1.0");

        RuleFor(x => x.Y)
            .InclusiveBetween(-1.0, 1.0)
            .WithMessage("Y coordinate must be between -1.0 and 1.0");

        RuleFor(x => x.Z)
            .InclusiveBetween(-1.0, 1.0)
            .WithMessage("Z coordinate must be between -1.0 and 1.0");

        RuleFor(x => x.K)
            .GreaterThan(0)
            .WithMessage("K must be greater than 0")
            .LessThanOrEqualTo(1000)
            .WithMessage("K cannot exceed 1000 for performance reasons");
    }
}
