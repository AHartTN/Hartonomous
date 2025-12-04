using FluentValidation;

namespace Hartonomous.Core.Application.Commands.Landmarks;

public sealed class CreateLandmarkCommandValidator : AbstractValidator<CreateLandmarkCommand>
{
    public CreateLandmarkCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Landmark name is required")
            .MaximumLength(256)
            .WithMessage("Landmark name cannot exceed 256 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2048)
            .WithMessage("Description cannot exceed 2048 characters");

        RuleFor(x => x.CenterX)
            .InclusiveBetween(-1.0, 1.0)
            .WithMessage("Center X coordinate must be between -1.0 and 1.0");

        RuleFor(x => x.CenterY)
            .InclusiveBetween(-1.0, 1.0)
            .WithMessage("Center Y coordinate must be between -1.0 and 1.0");

        RuleFor(x => x.CenterZ)
            .InclusiveBetween(-1.0, 1.0)
            .WithMessage("Center Z coordinate must be between -1.0 and 1.0");

        RuleFor(x => x.Radius)
            .GreaterThan(0)
            .WithMessage("Radius must be greater than 0")
            .LessThanOrEqualTo(2.0)
            .WithMessage("Radius cannot exceed 2.0 (diagonal of unit cube)");
    }
}
