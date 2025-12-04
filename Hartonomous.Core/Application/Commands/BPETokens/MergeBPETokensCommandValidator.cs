using FluentValidation;

namespace Hartonomous.Core.Application.Commands.BPETokens;

public sealed class MergeBPETokensCommandValidator : AbstractValidator<MergeBPETokensCommand>
{
    public MergeBPETokensCommandValidator()
    {
        RuleFor(x => x.ConstantSequence)
            .NotNull()
            .WithMessage("Constant sequence is required")
            .Must(seq => seq.Count >= 2)
            .WithMessage("At least 2 constants are required to create a BPE token")
            .Must(seq => seq.Count <= 1000)
            .WithMessage("Cannot merge more than 1000 constants at once")
            .Must(seq => seq.Distinct().Count() == seq.Count || seq.Count == seq.Distinct().Count())
            .WithMessage("Duplicate constants in sequence are allowed but verify intentional");
    }
}
