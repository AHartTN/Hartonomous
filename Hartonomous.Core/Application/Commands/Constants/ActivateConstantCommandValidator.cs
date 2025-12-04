using FluentValidation;

namespace Hartonomous.Core.Application.Commands.Constants;

public sealed class ActivateConstantCommandValidator : AbstractValidator<ActivateConstantCommand>
{
    public ActivateConstantCommandValidator()
    {
        RuleFor(x => x.ConstantId)
            .NotEmpty()
            .WithMessage("Constant ID is required");
    }
}
