using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Factory interface for obtaining appropriate content decomposer
/// </summary>
public interface IContentDecomposerFactory
{
    /// <summary>
    /// Get appropriate decomposer for given content and type
    /// </summary>
    IContentDecomposer GetDecomposer(byte[] data, ContentType contentType);
    
    /// <summary>
    /// Decompose content using appropriate strategy
    /// </summary>
    Task<List<Constant>> DecomposeAsync(byte[] data, ContentType contentType, CancellationToken cancellationToken = default);
}
