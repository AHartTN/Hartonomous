using Hartonomous.Core.Models;

namespace Hartonomous.Core.Services.Abstractions;

/// <summary>
/// Database operations interface for the Hartonomous substrate.
/// Provides access to encoding, decoding, ingestion, and query operations.
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    /// Initialize database connection and ensure schema exists.
    /// Idempotent - safe to call multiple times.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Get database statistics.
    /// </summary>
    DbStats GetStats();

    /// <summary>
    /// Ingest a file or directory.
    /// </summary>
    /// <param name="path">Path to file or directory to ingest.</param>
    /// <param name="sparsity">Sparsity threshold for model weights (default: 1e-6).</param>
    IngestResult Ingest(string path, double sparsity = 1e-6);

    /// <summary>
    /// Check if content exists in the substrate.
    /// </summary>
    bool ContentExists(string text);

    /// <summary>
    /// Encode text and store in substrate. Returns root ID.
    /// </summary>
    (long High, long Low) EncodeAndStore(string text);

    /// <summary>
    /// Decode a root ID back to original text.
    /// </summary>
    string Decode(long idHigh, long idLow);

    /// <summary>
    /// Complete a prompt using semantic graph traversal.
    /// Replaces LLM completion with O(log n) spatial lookups.
    /// </summary>
    string Complete(string prompt, int maxTokens = 20, double temperature = 0.7, ulong seed = 0);

    /// <summary>
    /// Ask a question and get an answer from the knowledge graph.
    /// Uses A* pathfinding through semantic relationships.
    /// </summary>
    (string Answer, double Confidence, InferenceHop[] Path) Ask(string question, int maxHops = 6);
}
