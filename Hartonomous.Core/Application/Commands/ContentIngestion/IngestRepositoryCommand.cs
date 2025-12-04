using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

/// <summary>
/// Command to ingest an entire Git repository
/// Recursively walks directory tree and ingests all files
/// </summary>
public sealed record IngestRepositoryCommand : ICommand<Result<IngestRepositoryResponse>>
{
    /// <summary>Local or remote path to repository</summary>
    public required string RepositoryPath { get; init; }
    
    /// <summary>Optional: specific branch/commit to ingest</summary>
    public string? Branch { get; init; }
    
    /// <summary>File patterns to include (e.g., "*.cs", "*.py")</summary>
    public List<string>? IncludePatterns { get; init; }
    
    /// <summary>File patterns to exclude (e.g., "*.dll", "bin/**")</summary>
    public List<string>? ExcludePatterns { get; init; }
    
    /// <summary>Maximum file size to ingest (bytes)</summary>
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024; // 10MB default
    
    /// <summary>Whether to learn BPE vocabulary from this repository</summary>
    public bool LearnBPE { get; init; } = true;
    
    /// <summary>Additional metadata about the repository</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Response for repository ingestion
/// </summary>
public sealed record IngestRepositoryResponse
{
    public required Guid BatchId { get; init; }
    public required int TotalFilesProcessed { get; init; }
    public required int TotalFilesSkipped { get; init; }
    public required long TotalBytesIngested { get; init; }
    public required int TotalConstantsCreated { get; init; }
    public required int UniqueConstantsCreated { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required long ProcessingTimeMs { get; init; }
    public List<string>? Errors { get; init; }
}
