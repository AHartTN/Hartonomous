using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.BPETokens;

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
