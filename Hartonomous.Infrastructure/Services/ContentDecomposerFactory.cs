using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Services;

/// <summary>
/// Factory for selecting appropriate content decomposer based on content type
/// Uses chain of responsibility pattern with auto-detection fallback
/// </summary>
public sealed class ContentDecomposerFactory : IContentDecomposerFactory
{
    private readonly IEnumerable<IContentDecomposer> _decomposers;
    private readonly ILogger<ContentDecomposerFactory> _logger;

    public ContentDecomposerFactory(
        IEnumerable<IContentDecomposer> decomposers,
        ILogger<ContentDecomposerFactory> logger)
    {
        _decomposers = decomposers ?? throw new ArgumentNullException(nameof(decomposers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IContentDecomposer GetDecomposer(byte[] data, ContentType contentType)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty", nameof(data));
        }

        // Try to find decomposer for declared content type
        var matchingDecomposer = _decomposers.FirstOrDefault(d => 
            d.SupportedContentType == contentType && d.CanDecompose(data, contentType));

        if (matchingDecomposer != null)
        {
            _logger.LogDebug("Selected {DecomposerType} for content type {ContentType}", 
                matchingDecomposer.GetType().Name, contentType);
            return matchingDecomposer;
        }

        // Auto-detect content type if declared type doesn't match
        _logger.LogWarning("No matching decomposer for declared type {ContentType}, attempting auto-detection", contentType);
        
        matchingDecomposer = _decomposers.FirstOrDefault(d => d.CanDecompose(data, d.SupportedContentType));

        if (matchingDecomposer != null)
        {
            _logger.LogInformation("Auto-detected content type {ContentType}, using {DecomposerType}",
                matchingDecomposer.SupportedContentType, matchingDecomposer.GetType().Name);
            return matchingDecomposer;
        }

        // Fallback to binary decomposer
        var binaryDecomposer = _decomposers.FirstOrDefault(d => d.SupportedContentType == ContentType.Binary);
        if (binaryDecomposer == null)
        {
            throw new InvalidOperationException("Binary decomposer not registered - cannot decompose content");
        }

        _logger.LogWarning("No suitable decomposer found, falling back to binary decomposition");
        return binaryDecomposer;
    }

    public async Task<List<Constant>> DecomposeAsync(
        byte[] data, 
        ContentType contentType, 
        CancellationToken cancellationToken = default)
    {
        var decomposer = GetDecomposer(data, contentType);
        return await decomposer.DecomposeAsync(data, cancellationToken);
    }
}
