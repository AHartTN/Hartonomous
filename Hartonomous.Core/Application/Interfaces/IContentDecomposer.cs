using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Interface for content decomposition strategies
/// Different implementations handle different content types (text, image, audio, video, binary)
/// </summary>
public interface IContentDecomposer
{
    /// <summary>
    /// Content type this decomposer handles
    /// </summary>
    ContentType SupportedContentType { get; }
    
    /// <summary>
    /// Decompose content into atomic constants
    /// </summary>
    /// <param name="data">Raw content data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of constant entities</returns>
    Task<List<Constant>> DecomposeAsync(byte[] data, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Determine if this decomposer can handle the given content
    /// </summary>
    bool CanDecompose(byte[] data, ContentType declaredType);
}
