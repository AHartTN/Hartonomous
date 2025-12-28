namespace Hartonomous.Core.Models;

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

/// <summary>
/// A hop in the inference path.
/// </summary>
public readonly record struct InferenceHop(
    string FromText,
    string ToText,
    double Weight);
