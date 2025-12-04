using FluentValidation;
using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

public sealed class IngestContentCommandValidator : AbstractValidator<IngestContentCommand>
{
    private const int MaxContentSizeBytes = 1024 * 1024 * 100; // 100 MB
    private const int MinContentSizeBytes = 1; // At least 1 byte

    public IngestContentCommandValidator()
    {
        RuleFor(x => x.ContentData)
            .NotNull()
            .WithMessage("Content data is required")
            .Must(data => data.Length >= MinContentSizeBytes)
            .WithMessage($"Content must be at least {MinContentSizeBytes} byte")
            .Must(data => data.Length <= MaxContentSizeBytes)
            .WithMessage($"Content size cannot exceed {MaxContentSizeBytes / (1024 * 1024)} MB");

        RuleFor(x => x.ContentType)
            .IsInEnum()
            .WithMessage("Invalid content type");

        RuleFor(x => x.SourceUri)
            .MaximumLength(2048)
            .WithMessage("Source URI cannot exceed 2048 characters")
            .Must(BeValidUriIfProvided)
            .WithMessage("Source URI must be a valid URI format");

        RuleFor(x => x.Metadata)
            .Must(metadata => metadata == null || metadata.Count <= 50)
            .WithMessage("Metadata cannot contain more than 50 entries")
            .Must(metadata => metadata == null || metadata.All(kvp => kvp.Key.Length <= 256 && kvp.Value.Length <= 4096))
            .WithMessage("Metadata keys must be ≤256 chars and values ≤4096 chars");
    }

    private bool BeValidUriIfProvided(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return true;

        return Uri.TryCreate(uri, UriKind.Absolute, out _);
    }
}
