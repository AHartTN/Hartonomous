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

/// <summary>
/// Result of BPE vocabulary learning
/// </summary>
public sealed record LearnBPEVocabularyCommandResult
{
    public required int TokensLearned { get; init; }
    public required long TotalPairsAnalyzed { get; init; }
    public required long ProcessingTimeMs { get; init; }
    public required bool UsedGpu { get; init; }
}
