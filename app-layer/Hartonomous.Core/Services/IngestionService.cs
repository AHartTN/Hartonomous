using Hartonomous.Marshal;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Services;

/// <summary>
/// Wraps text/file ingestion via native C++ interop.
/// </summary>
public sealed class IngestionService : IDisposable
{
    private readonly EngineService _engine;
    private readonly IntPtr _ingesterHandle;
    private readonly ILogger<IngestionService> _logger;
    private bool _disposed;

    public IngestionService(EngineService engine, ILogger<IngestionService> logger)
    {
        _engine = engine;
        _logger = logger;
        _ingesterHandle = NativeMethods.IngesterCreate(engine.DbHandle);
        if (_ingesterHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create ingester: {EngineService.GetLastError()}");
    }

    public IngestionOutput IngestText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeMethods.IngestText(_ingesterHandle, text, out var stats))
            throw new InvalidOperationException($"Text ingestion failed: {EngineService.GetLastError()}");

        return MapStats(stats);
    }

    public IngestionOutput IngestFile(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeMethods.IngestFile(_ingesterHandle, filePath, out var stats))
            throw new InvalidOperationException($"File ingestion failed: {EngineService.GetLastError()}");

        return MapStats(stats);
    }

    private static IngestionOutput MapStats(IngestionStats stats) => new()
    {
        AtomsTotal = (long)stats.AtomsTotal,
        AtomsNew = (long)stats.AtomsNew,
        CompositionsTotal = (long)stats.CompositionsTotal,
        CompositionsNew = (long)stats.CompositionsNew,
        RelationsTotal = (long)stats.RelationsTotal,
        RelationsNew = (long)stats.RelationsNew,
        EvidenceCount = (long)stats.EvidenceCount,
        OriginalBytes = (long)stats.OriginalBytes,
        StoredBytes = (long)stats.StoredBytes,
        CompressionRatio = stats.CompressionRatio,
        NgramsExtracted = (long)stats.NgramsExtracted,
        NgramsSignificant = (long)stats.NgramsSignificant,
        CooccurrencesFound = (long)stats.CooccurrencesFound,
        CooccurrencesSignificant = (long)stats.CooccurrencesSignificant,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.IngesterDestroy(_ingesterHandle);
    }
}

public sealed class IngestionOutput
{
    public long AtomsTotal { get; init; }
    public long AtomsNew { get; init; }
    public long CompositionsTotal { get; init; }
    public long CompositionsNew { get; init; }
    public long RelationsTotal { get; init; }
    public long RelationsNew { get; init; }
    public long EvidenceCount { get; init; }
    public long OriginalBytes { get; init; }
    public long StoredBytes { get; init; }
    public double CompressionRatio { get; init; }
    public long NgramsExtracted { get; init; }
    public long NgramsSignificant { get; init; }
    public long CooccurrencesFound { get; init; }
    public long CooccurrencesSignificant { get; init; }
}
