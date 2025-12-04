using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using MediatR;
using System.Diagnostics;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

public sealed class IngestContentCommandHandler : IRequestHandler<IngestContentCommand, Result<IngestContentResponse>>
{
    private readonly IConstantRepository _constantRepository;
    private readonly IContentIngestionRepository _ingestionRepository;
    private readonly IContentDecomposerFactory _decomposerFactory;
    private readonly IUnitOfWork _unitOfWork;

    public IngestContentCommandHandler(
        IConstantRepository constantRepository,
        IContentIngestionRepository ingestionRepository,
        IContentDecomposerFactory decomposerFactory,
        IUnitOfWork unitOfWork)
    {
        _constantRepository = constantRepository;
        _ingestionRepository = ingestionRepository;
        _decomposerFactory = decomposerFactory;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IngestContentResponse>> Handle(IngestContentCommand request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Compute content hash
            var contentHash = Hash256.Compute(request.ContentData);

            // Check for duplicate ingestion
            var existingIngestion = await _ingestionRepository.GetByContentHashAsync(contentHash, cancellationToken);
            if (existingIngestion != null && existingIngestion.IsSuccessful)
            {
                return Result<IngestContentResponse>.Success(new IngestContentResponse
                {
                    IngestionId = existingIngestion.Id,
                    ContentHash = contentHash.ToString(),
                    TotalConstantsCreated = existingIngestion.ConstantCount,
                    UniqueConstantsCreated = existingIngestion.UniqueConstantCount,
                    DeduplicationRatio = existingIngestion.DeduplicationRatio,
                    ProcessingTimeMs = existingIngestion.ProcessingTimeMs
                });
            }

            // Create ingestion record
            var ingestion = Domain.Entities.ContentIngestion.Create(
                content: request.ContentData,
                contentType: request.ContentType,
                sourceIdentifier: request.SourceUri,
                metadata: request.Metadata != null ? System.Text.Json.JsonSerializer.Serialize(request.Metadata) : null
            );

            // Decompose content using appropriate strategy
            var decomposedConstants = await _decomposerFactory.DecomposeAsync(
                request.ContentData, 
                request.ContentType, 
                cancellationToken);

            // Batch deduplication - group by hash
            var constantsByHash = decomposedConstants
                .GroupBy(c => c.Hash.ToString())
                .ToDictionary(g => g.Key, g => g.ToList());

            var newConstants = new List<Constant>();
            var constantIds = new List<Guid>();
            var deduplicatedCount = 0;

            // Process in batches for better database performance
            const int batchSize = 100;
            var hashBatches = constantsByHash.Keys.Chunk(batchSize);

            foreach (var hashBatch in hashBatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Fetch existing constants for this batch
                var existingConstantsDict = new Dictionary<string, Constant>();
                foreach (var hashStr in hashBatch)
                {
                    var hash = Hash256.FromHex(hashStr);
                    var existing = await _constantRepository.GetByHashAsync(hash, cancellationToken);
                    if (existing != null)
                    {
                        existingConstantsDict[hashStr] = existing;
                    }
                }

                // Process constants in this batch
                foreach (var hashStr in hashBatch)
                {
                    var constantGroup = constantsByHash[hashStr];
                    
                    if (existingConstantsDict.TryGetValue(hashStr, out var existing))
                    {
                        // Constant already exists - increment reference count
                        existing.IncrementReferenceCount();
                        existing.IncrementFrequency();
                        
                        // Add ID for each occurrence in this content
                        for (int i = 0; i < constantGroup.Count; i++)
                        {
                            constantIds.Add(existing.Id);
                        }
                        
                        deduplicatedCount += constantGroup.Count;
                    }
                    else
                    {
                        // New constant - use first from group (they're identical by hash)
                        var constant = constantGroup[0];
                        
                        // Set frequency based on occurrences in this content
                        for (int i = 1; i < constantGroup.Count; i++)
                        {
                            constant.IncrementFrequency();
                        }
                        
                        newConstants.Add(constant);
                        
                        // Add ID for each occurrence
                        for (int i = 0; i < constantGroup.Count; i++)
                        {
                            constantIds.Add(constant.Id);
                        }
                    }
                }
            }

            // Batch insert new constants
            if (newConstants.Any())
            {
                await _constantRepository.AddRangeAsync(newConstants, cancellationToken);
            }

            // Record statistics
            var totalConstants = decomposedConstants.Count;
            var uniqueConstants = newConstants.Count;
            ingestion.RecordConstants(constantIds, uniqueConstants);

            stopwatch.Stop();
            ingestion.Complete(stopwatch.ElapsedMilliseconds);

            // Save ingestion record
            await _ingestionRepository.AddAsync(ingestion, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<IngestContentResponse>.Success(new IngestContentResponse
            {
                IngestionId = ingestion.Id,
                ContentHash = contentHash.ToString(),
                TotalConstantsCreated = totalConstants,
                UniqueConstantsCreated = uniqueConstants,
                DeduplicationRatio = ingestion.DeduplicationRatio,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            return Result<IngestContentResponse>.Failure($"Failed to ingest content: {ex.Message}");
        }
    }
}
