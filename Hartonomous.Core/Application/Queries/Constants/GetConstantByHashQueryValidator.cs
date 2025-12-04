using FluentValidation;

namespace Hartonomous.Core.Application.Queries.Constants;

public sealed class GetConstantByHashQueryValidator : AbstractValidator<GetConstantByHashQuery>
{
    public GetConstantByHashQueryValidator()
    {
        RuleFor(x => x.Hash)
            .NotEmpty()
            .WithMessage("Hash is required")
            .Length(64)
            .WithMessage("Hash must be 64 characters (SHA-256 hex)")
            .Matches("^[a-fA-F0-9]{64}$")
            .WithMessage("Hash must be a valid hexadecimal string");
    }
}
