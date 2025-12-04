using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

public sealed class IngestRepositoryCommandHandler : IRequestHandler<IngestRepositoryCommand, Result<IngestRepositoryResponse>>
{
    private readonly IMediator _mediator;
    private readonly IBPEService _bpeService;
    private readonly IConstantRepository _constantRepository;
    private readonly ILogger<IngestRepositoryCommandHandler> _logger;

    public IngestRepositoryCommandHandler(
        IMediator mediator,
        IBPEService bpeService,
        IConstantRepository constantRepository,
        ILogger<IngestRepositoryCommandHandler> logger)
    {
        _mediator = mediator;
        _bpeService = bpeService;
        _constantRepository = constantRepository;
        _logger = logger;
    }

    public async Task<Result<IngestRepositoryResponse>> Handle(
        IngestRepositoryCommand request, 
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var batchId = Guid.NewGuid();
        var errors = new List<string>();

        _logger.LogInformation("Starting repository ingestion: {Path}", request.RepositoryPath);

        try
        {
            // Validate path exists
            if (!Directory.Exists(request.RepositoryPath))
            {
                return Result<IngestRepositoryResponse>.Failure($"Repository path does not exist: {request.RepositoryPath}");
            }

            // Set default patterns if not provided
            var includePatterns = request.IncludePatterns ?? new List<string> { "*.*" };
            var excludePatterns = request.ExcludePatterns ?? new List<string>
            {
                "*.dll", "*.exe", "*.so", "*.dylib", "*.bin", "*.obj", "*.o",
                "**/bin/**", "**/obj/**", "**/node_modules/**", "**/.git/**",
                "**/artifacts/**", "**/packages/**", "**/.vs/**"
            };

            // Walk directory tree
            var files = GetMatchingFiles(
                request.RepositoryPath, 
                includePatterns, 
                excludePatterns, 
                request.MaxFileSizeBytes);

            _logger.LogInformation("Found {Count} files to ingest", files.Count);

            // Track statistics
            int filesProcessed = 0;
            int filesSkipped = 0;
            long totalBytesIngested = 0;
            int totalConstantsCreated = 0;
            int uniqueConstantsCreated = 0;

            // Process each file
            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var fileContent = await File.ReadAllBytesAsync(filePath, cancellationToken);

                    // Determine content type
                    var contentType = GetContentType(fileInfo.Extension);

                    // Create metadata
                    var metadata = new Dictionary<string, string>
                    {
                        ["FileName"] = fileInfo.Name,
                        ["FilePath"] = filePath,
                        ["FileExtension"] = fileInfo.Extension,
                        ["FileSize"] = fileInfo.Length.ToString(),
                        ["BatchId"] = batchId.ToString()
                    };

                    // Add any additional metadata from request
                    if (request.Metadata != null)
                    {
                        foreach (var kvp in request.Metadata)
                        {
                            metadata[kvp.Key] = kvp.Value;
                        }
                    }

                    // Ingest file content
                    var ingestCommand = new IngestContentCommand
                    {
                        ContentData = fileContent,
                        ContentType = contentType,
                        SourceUri = filePath,
                        Metadata = metadata
                    };

                    var result = await _mediator.Send(ingestCommand, cancellationToken);

                    if (result.IsSuccess && result.Value != null)
                    {
                        filesProcessed++;
                        totalBytesIngested += fileInfo.Length;
                        totalConstantsCreated += result.Value.TotalConstantsCreated;
                        uniqueConstantsCreated += result.Value.UniqueConstantsCreated;

                        _logger.LogDebug("Ingested file: {Path} ({Size} bytes, {Constants} constants)",
                            filePath, fileInfo.Length, result.Value.TotalConstantsCreated);
                    }
                    else
                    {
                        filesSkipped++;
                        var errorMsg = $"Failed to ingest {filePath}: {result.Error}";
                        errors.Add(errorMsg);
                        _logger.LogWarning(errorMsg);
                    }
                }
                catch (Exception ex)
                {
                    filesSkipped++;
                    var errorMsg = $"Error ingesting {filePath}: {ex.Message}";
                    errors.Add(errorMsg);
                    _logger.LogError(ex, "Error ingesting file: {Path}", filePath);
                }
            }

            // Learn BPE vocabulary from all ingested constants
            if (request.LearnBPE && uniqueConstantsCreated > 0)
            {
                _logger.LogInformation("Learning BPE vocabulary from {Count} unique constants", uniqueConstantsCreated);
                
                try
                {
                    var tokensLearned = await _bpeService.RefreshVocabularyAsync(cancellationToken);
                    _logger.LogInformation("Learned {Count} BPE tokens from repository", tokensLearned);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to learn BPE vocabulary");
                    errors.Add($"BPE learning failed: {ex.Message}");
                }
            }

            stopwatch.Stop();

            var deduplicationRatio = totalConstantsCreated > 0 
                ? (double)uniqueConstantsCreated / totalConstantsCreated 
                : 0.0;

            _logger.LogInformation(
                "Repository ingestion complete: {Processed} files, {Bytes} bytes, {Total} constants, {Unique} unique ({Ratio:P2} dedup) in {Ms}ms",
                filesProcessed, totalBytesIngested, totalConstantsCreated, uniqueConstantsCreated, 
                deduplicationRatio, stopwatch.ElapsedMilliseconds);

            return Result<IngestRepositoryResponse>.Success(new IngestRepositoryResponse
            {
                BatchId = batchId,
                TotalFilesProcessed = filesProcessed,
                TotalFilesSkipped = filesSkipped,
                TotalBytesIngested = totalBytesIngested,
                TotalConstantsCreated = totalConstantsCreated,
                UniqueConstantsCreated = uniqueConstantsCreated,
                DeduplicationRatio = deduplicationRatio,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Errors = errors.Any() ? errors : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repository ingestion failed: {Path}", request.RepositoryPath);
            return Result<IngestRepositoryResponse>.Failure($"Repository ingestion failed: {ex.Message}");
        }
    }

    private List<string> GetMatchingFiles(
        string rootPath, 
        List<string> includePatterns, 
        List<string> excludePatterns, 
        long maxFileSize)
    {
        var matchedFiles = new List<string>();
        int totalFiles = 0;
        int sizeFiltered = 0;
        int excludeFiltered = 0;
        int includeFiltered = 0;

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
        {
            totalFiles++;
            
            // Check file size
            var fileInfo = new FileInfo(file);
            if (fileInfo.Length > maxFileSize)
            {
                sizeFiltered++;
                continue;
            }

            // Check exclude patterns
            var relativePath = Path.GetRelativePath(rootPath, file);
            if (excludePatterns.Any(pattern => MatchesPattern(relativePath, pattern)))
            {
                excludeFiltered++;
                continue;
            }

            // Check include patterns
            if (includePatterns.Any(pattern => MatchesPattern(relativePath, pattern)))
            {
                matchedFiles.Add(file);
            }
            else
            {
                includeFiltered++;
            }
        }

        _logger.LogDebug(
            "File filtering: {Total} total, {Size} too large, {Exclude} excluded, {Include} didn't match include patterns, {Matched} matched",
            totalFiles, sizeFiltered, excludeFiltered, includeFiltered, matchedFiles.Count);

        return matchedFiles;
    }

    private bool MatchesPattern(string path, string pattern)
    {
        // Normalize path separators for consistent matching
        var normalizedPath = path.Replace('\\', '/');
        var normalizedPattern = pattern.Replace('\\', '/');
        
        // Handle ** (directory wildcard)
        if (normalizedPattern.Contains("**"))
        {
            var parts = normalizedPattern.Split("**", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return true; // Just "**" matches everything
            
            if (parts.Length == 1)
            {
                // Pattern like "**/bin" or "bin/**"
                var part = parts[0].Trim('/');
                return normalizedPath.Contains($"/{part}/") || 
                       normalizedPath.Contains($"/{part}") ||
                       normalizedPath.StartsWith($"{part}/");
            }
            
            // Pattern like "src/**/test"
            return normalizedPath.Contains(parts[0].TrimEnd('/')) && 
                   normalizedPath.Contains(parts[1].TrimStart('/'));
        }

        // Handle * wildcard (single directory/file level)
        if (normalizedPattern.StartsWith("*."))
        {
            // Extension match
            return normalizedPath.EndsWith(normalizedPattern.Substring(1), StringComparison.OrdinalIgnoreCase);
        }

        // Handle *.*
        if (normalizedPattern == "*.*")
        {
            return true;
        }

        // Exact match
        return normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private ContentType GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".rst" => ContentType.Text,
            ".cs" or ".py" or ".js" or ".ts" or ".java" or ".cpp" or ".c" or ".h" or ".go" or ".rs" => ContentType.Code,
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" => ContentType.Text,
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => ContentType.Image,
            ".mp3" or ".wav" or ".ogg" or ".flac" => ContentType.Audio,
            ".mp4" or ".avi" or ".mkv" or ".webm" => ContentType.Video,
            _ => ContentType.Binary
        };
    }
}
