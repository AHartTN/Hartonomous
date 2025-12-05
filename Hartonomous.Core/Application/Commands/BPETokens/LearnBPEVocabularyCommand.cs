using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.BPETokens;

/// <summary>
/// Command to learn BPE vocabulary from content
/// </summary>
public sealed record LearnBPEVocabularyCommand : ICommand<Result<LearnBPEVocabularyCommandResult>>
{
    public required int MaxVocabSize { get; init; }
    public required int MinFrequency { get; init; }
    public required int SampleSize { get; init; }
    public required bool UseGpu { get; init; }
}
