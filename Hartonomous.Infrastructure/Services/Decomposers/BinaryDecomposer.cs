using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Services.Decomposers;

/// <summary>
/// Binary content decomposer - byte-level atomic decomposition
/// This is the fallback decomposer for unknown or raw binary content
/// </summary>
public sealed class BinaryDecomposer : IContentDecomposer
{
    private readonly ILogger<BinaryDecomposer> _logger;
    private readonly IQuantizationService _quantizationService;
    
    public ContentType SupportedContentType => ContentType.Binary;

    public BinaryDecomposer(
        ILogger<BinaryDecomposer> logger,
        IQuantizationService quantizationService)
    {
        _logger = logger;
        _quantizationService = quantizationService;
    }

    public Task<List<Constant>> DecomposeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty", nameof(data));
        }

        _logger.LogDebug("Decomposing binary content: {Size} bytes", data.Length);

        var constants = new List<Constant>(data.Length);
        
        // Decompose into individual bytes
        for (int i = 0; i < data.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var byteData = new byte[] { data[i] };
            var constant = Constant.Create(byteData, ContentType.Binary);
            
            // Quantize and project to 4D spatial coordinates
            var (y, z, m) = _quantizationService.Quantize(byteData);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            
            constants.Add(constant);
        }

        _logger.LogInformation("Binary decomposition complete: {ConstantCount} constants", constants.Count);
        
        return Task.FromResult(constants);
    }

    public bool CanDecompose(byte[] data, ContentType declaredType)
    {
        // Binary decomposer can handle any content
        return true;
    }
}
