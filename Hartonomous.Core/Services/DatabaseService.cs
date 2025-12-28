using System.Text;
using Hartonomous.Core.Models;
using Hartonomous.Core.Native;
using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Core.Services;

/// <summary>
/// Database operations via native library.
/// Provides direct access to the Hartonomous substrate without subprocess hacks.
/// </summary>
public sealed class DatabaseService : IDatabaseService
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

    /// <summary>
    /// Complete a prompt using semantic graph traversal.
    /// Replaces LLM completion with O(log n) spatial lookups.
    /// </summary>
    public string Complete(string prompt, int maxTokens = 20, double temperature = 0.7, ulong seed = 0)
    {
        EnsureInitialized();

        var promptBytes = Encoding.UTF8.GetBytes(prompt);
        var buffer = new byte[maxTokens * 100];  // Generous buffer for output

        var result = NativeInterop.Complete(
            promptBytes, promptBytes.Length,
            maxTokens, temperature, seed,
            buffer, buffer.Length,
            out var generatedLen);

        if (result < 0)
        {
            throw new InvalidOperationException($"Completion failed: error {result}");
        }

        return Encoding.UTF8.GetString(buffer, 0, generatedLen);
    }

    /// <summary>
    /// Ask a question and get an answer from the knowledge graph.
    /// Uses A* pathfinding through semantic relationships.
    /// </summary>
    public (string Answer, double Confidence, InferenceHop[] Path) Ask(string question, int maxHops = 6)
    {
        EnsureInitialized();

        var questionBytes = Encoding.UTF8.GetBytes(question);
        var buffer = new byte[16 * 1024];  // 16KB for answer
        var pathBuffer = new NativeInterop.NativeInferenceHop[maxHops];

        var result = NativeInterop.Ask(
            questionBytes, questionBytes.Length,
            maxHops,
            buffer, buffer.Length,
            out var answerLen,
            out var confidence);

        if (result == 1)
        {
            // No answer found
            return ("", 0.0, []);
        }

        if (result < 0)
        {
            throw new InvalidOperationException($"Ask failed: error {result}");
        }

        var answer = Encoding.UTF8.GetString(buffer, 0, answerLen);

        // Get the inference path for verbose output
        var questionRef = EncodeAndStore(question);
        var hops = new List<InferenceHop>();

        // For now, return empty path - would need additional native call for path details
        return (answer, confidence, []);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }
}
