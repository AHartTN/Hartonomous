using System.Text;
using Hartonomous.Core.Native;

namespace Hartonomous.Core.Services;

/// <summary>
/// Database operations via native library.
/// Provides direct access to the Hartonomous substrate without subprocess hacks.
/// </summary>
public sealed class DatabaseService
{
    private static readonly Lazy<DatabaseService> _instance = new(() => new DatabaseService());
    private bool _initialized;
    private readonly object _lock = new();

    public static DatabaseService Instance => _instance.Value;

    private DatabaseService() { }

    /// <summary>
    /// Initialize database connection and ensure schema exists.
    /// Idempotent - safe to call multiple times.
    /// </summary>
    public void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;
            
            var result = NativeInterop.DbInit();
            if (result < 0)
            {
                throw new InvalidOperationException($"Database initialization failed: error {result}");
            }
            _initialized = true;
        }
    }

    /// <summary>
    /// Get database statistics.
    /// </summary>
    public DbStats GetStats()
    {
        EnsureInitialized();
        
        var result = NativeInterop.DbStats(out var stats);
        if (result < 0)
        {
            throw new InvalidOperationException($"Failed to get database stats: error {result}");
        }

        return new DbStats(
            stats.AtomCount,
            stats.CompositionCount,
            stats.RelationshipCount,
            stats.DatabaseSizeBytes);
    }

    /// <summary>
    /// Ingest a file or directory.
    /// </summary>
    public IngestResult Ingest(string path, double sparsity = 1e-6)
    {
        EnsureInitialized();

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException($"Path not found: {fullPath}");
        }

        var result = NativeInterop.Ingest(fullPath, sparsity, out var nativeResult);
        if (result < 0)
        {
            throw new InvalidOperationException($"Ingestion failed: error {result}");
        }

        return new IngestResult(
            nativeResult.FilesProcessed,
            nativeResult.BytesProcessed,
            nativeResult.CompositionsCreated,
            nativeResult.RelationshipsCreated,
            nativeResult.Errors,
            TimeSpan.FromMilliseconds(nativeResult.DurationMs));
    }

    /// <summary>
    /// Check if content exists in the substrate.
    /// </summary>
    public bool ContentExists(string text)
    {
        EnsureInitialized();

        var bytes = Encoding.UTF8.GetBytes(text);
        var result = NativeInterop.ContentExists(bytes, bytes.Length, out var exists);
        if (result < 0)
        {
            throw new InvalidOperationException($"Content check failed: error {result}");
        }

        return exists != 0;
    }

    /// <summary>
    /// Encode text and store in substrate. Returns root ID.
    /// </summary>
    public (long High, long Low) EncodeAndStore(string text)
    {
        EnsureInitialized();

        var bytes = Encoding.UTF8.GetBytes(text);
        var result = NativeInterop.EncodeAndStore(bytes, bytes.Length, out var high, out var low);
        if (result < 0)
        {
            throw new InvalidOperationException($"Encode failed: error {result}");
        }

        return (high, low);
    }

    /// <summary>
    /// Decode a root ID back to original text.
    /// </summary>
    public string Decode(long idHigh, long idLow)
    {
        EnsureInitialized();

        // Start with reasonable buffer, grow if needed
        var buffer = new byte[64 * 1024];  // 64KB
        var result = NativeInterop.Decode(idHigh, idLow, buffer, buffer.Length, out var textLen);

        if (result == -1 && textLen > buffer.Length)
        {
            // Buffer too small, retry with correct size
            buffer = new byte[textLen];
            result = NativeInterop.Decode(idHigh, idLow, buffer, buffer.Length, out textLen);
        }

        if (result < 0)
        {
            throw new InvalidOperationException($"Decode failed: error {result}");
        }

        return Encoding.UTF8.GetString(buffer, 0, textLen);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }
}

/// <summary>
/// Database statistics.
/// </summary>
public readonly record struct DbStats(
    long AtomCount,
    long CompositionCount,
    long RelationshipCount,
    long DatabaseSizeBytes);

/// <summary>
/// Ingestion result.
/// </summary>
public readonly record struct IngestResult(
    long FilesProcessed,
    long BytesProcessed,
    long CompositionsCreated,
    long RelationshipsCreated,
    long Errors,
    TimeSpan Duration);
