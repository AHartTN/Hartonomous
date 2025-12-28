using System.Runtime.InteropServices;

namespace Hartonomous.Core.Native;

/// <summary>
/// P/Invoke declarations for the native Hartonomous.Native library.
/// All heavy computation happens in C++; this is just the interop layer.
/// On Windows: Hartonomous.Native.dll
/// On Linux/macOS: libHartonomous.Native.so / libHartonomous.Native.dylib
/// </summary>
internal static partial class NativeInterop
{
#if WINDOWS
    private const string LibraryName = "Hartonomous.Native";
#else
    private const string LibraryName = "libHartonomous.Native";
#endif

    // =========================================================================
    // COORDINATE MAPPING
    // =========================================================================

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_map_codepoint")]
    internal static partial int MapCodepoint(int codepoint, out NativeAtom result);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_map_codepoint_range")]
    internal static partial int MapCodepointRange(
        int start,
        int end,
        [Out] NativeAtom[] results,
        int resultsCapacity);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_get_hilbert_index")]
    internal static partial int GetHilbertIndex(int codepoint, out long high, out long low);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_coords_to_hilbert")]
    internal static partial int CoordsToHilbert(
        uint x, uint y, uint z, uint w,
        out long high, out long low);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_hilbert_to_coords")]
    internal static partial int HilbertToCoords(
        long high, long low,
        out uint x, out uint y, out uint z, out uint w);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_is_valid_scalar")]
    internal static partial int IsValidScalar(int codepoint);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_get_codepoint_count")]
    internal static partial int GetCodepointCount();

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_get_max_codepoint")]
    internal static partial int GetMaxCodepoint();

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_get_version")]
    private static partial nint GetVersionPtr();

    /// <summary>
    /// Gets the native library version string.
    /// Uses manual marshalling because the native function returns a static const char*
    /// that must NOT be freed by the runtime.
    /// </summary>
    internal static string GetVersion()
    {
        var ptr = GetVersionPtr();
        return ptr == 0 ? "unknown" : Marshal.PtrToStringUTF8(ptr) ?? "unknown";
    }

    // =========================================================================
    // DATABASE OPERATIONS
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDbStats
    {
        public long AtomCount;
        public long CompositionCount;
        public long RelationshipCount;
        public long DatabaseSizeBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeIngestResult
    {
        public long FilesProcessed;
        public long BytesProcessed;
        public long CompositionsCreated;
        public long RelationshipsCreated;
        public long Errors;
        public long DurationMs;
    }

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_db_init")]
    internal static partial int DbInit();

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_db_stats")]
    internal static partial int DbStats(out NativeDbStats stats);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_ingest", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Ingest(string path, double sparsity, out NativeIngestResult result);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_content_exists")]
    internal static partial int ContentExists(
        [In] byte[] text,
        int textLen,
        out int exists);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_encode_and_store")]
    internal static partial int EncodeAndStore(
        [In] byte[] text,
        int textLen,
        out long idHigh,
        out long idLow);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_decode")]
    internal static partial int Decode(
        long idHigh,
        long idLow,
        [Out] byte[] buffer,
        int bufferCapacity,
        out int textLen);

    // =========================================================================
    // SPATIAL QUERIES
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSpatialMatch
    {
        public long HilbertHigh;
        public long HilbertLow;
        public int Codepoint;
        public double Distance;
    }

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_similar")]
    internal static partial int FindSimilar(
        int codepoint,
        [Out] NativeSpatialMatch[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_near")]
    internal static partial int FindNear(
        int codepoint,
        double distanceThreshold,
        [Out] NativeSpatialMatch[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_case_variants")]
    internal static partial int FindCaseVariants(
        int codepoint,
        [Out] NativeSpatialMatch[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_diacritical_variants")]
    internal static partial int FindDiacriticalVariants(
        int codepoint,
        [Out] NativeSpatialMatch[] results,
        int capacity,
        out int count);

    // =========================================================================
    // RELATIONSHIP QUERIES
    // =========================================================================

    internal enum NativeRelType : short
    {
        SemanticLink = 0,
        ModelWeight = 1,
        KnowledgeEdge = 2,
        TemporalNext = 3,
        SpatialNear = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRelationship
    {
        public long FromHigh;
        public long FromLow;
        public long ToHigh;
        public long ToLow;
        public double Weight;
        public short RelType;
        public long ContextHigh;
        public long ContextLow;
    }

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_store_relationship")]
    internal static partial int StoreRelationship(
        long fromHigh, long fromLow,
        long toHigh, long toLow,
        double weight,
        short relType,
        long contextHigh, long contextLow);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_from")]
    internal static partial int FindFrom(
        long fromHigh, long fromLow,
        [Out] NativeRelationship[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_to")]
    internal static partial int FindTo(
        long toHigh, long toLow,
        [Out] NativeRelationship[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_by_type")]
    internal static partial int FindByType(
        long fromHigh, long fromLow,
        short relType,
        [Out] NativeRelationship[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_by_weight")]
    internal static partial int FindByWeight(
        double minWeight, double maxWeight,
        long contextHigh, long contextLow,
        [Out] NativeRelationship[] results,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_get_weight")]
    internal static partial int GetWeight(
        long fromHigh, long fromLow,
        long toHigh, long toLow,
        long contextHigh, long contextLow,
        out double weight);

    // =========================================================================
    // TRAJECTORY QUERIES
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTrajectoryPoint
    {
        public short Page;
        public short Type;
        public int Base;
        public byte Variant;
        public uint Count;
    }

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_build_trajectory")]
    internal static partial int BuildTrajectory(
        [In] byte[] text,
        int textLen,
        [Out] NativeTrajectoryPoint[] points,
        int capacity,
        out int pointCount);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_store_trajectory")]
    internal static partial int StoreTrajectory(
        long fromHigh, long fromLow,
        long toHigh, long toLow,
        [In] NativeTrajectoryPoint[] points,
        int pointCount,
        double weight,
        short relType,
        long contextHigh, long contextLow);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_trajectory_to_text")]
    internal static partial int TrajectoryToText(
        [In] NativeTrajectoryPoint[] points,
        int pointCount,
        [Out] byte[] buffer,
        int bufferCapacity,
        out int textLen);

    // =========================================================================
    // CONTAINMENT QUERIES
    // =========================================================================

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_contains_substring")]
    internal static partial int ContainsSubstring(
        [In] byte[] text,
        int textLen,
        out int exists);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_find_containing")]
    internal static partial int FindContaining(
        [In] byte[] text,
        int textLen,
        [Out] long[] results,
        int capacity,
        out int count);

    // =========================================================================
    // MLOPS - INFERENCE & GENERATION
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeCandidate
    {
        public long HilbertHigh;
        public long HilbertLow;
        public double Score;
        public int Rank;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInferenceHop
    {
        public long FromHigh;
        public long FromLow;
        public long ToHigh;
        public long ToLow;
        public double Weight;
        public double CumulativeCost;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeAttendedNode
    {
        public long HilbertHigh;
        public long HilbertLow;
        public double AttentionWeight;
    }

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_generate")]
    internal static partial int Generate(
        long contextHigh, long contextLow,
        int topK,
        double temperature,
        [Out] NativeCandidate[] candidates,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_generate_next")]
    internal static partial int GenerateNext(
        [In] long[] contextHighs,
        [In] long[] contextLows,
        int contextLen,
        int topK,
        double temperature,
        [Out] NativeCandidate[] candidates,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_infer")]
    internal static partial int Infer(
        long startHigh, long startLow,
        int maxHops,
        [Out] NativeInferenceHop[] hops,
        int capacity,
        out int hopCount);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_infer_to")]
    internal static partial int InferTo(
        long startHigh, long startLow,
        long goalHigh, long goalLow,
        int maxHops,
        [Out] NativeInferenceHop[] hops,
        int capacity,
        out int hopCount);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_attend")]
    internal static partial int Attend(
        [In] long[] queryHighs,
        [In] long[] queryLows,
        int queryLen,
        [In] long[] keyHighs,
        [In] long[] keyLows,
        int keyLen,
        [Out] NativeAttendedNode[] attended,
        int capacity,
        out int count);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_complete")]
    internal static partial int Complete(
        [In] byte[] prompt,
        int promptLen,
        int maxTokens,
        double temperature,
        ulong seed,
        [Out] byte[] buffer,
        int bufferCapacity,
        out int generatedLen);

    [LibraryImport(LibraryName, EntryPoint = "hartonomous_ask")]
    internal static partial int Ask(
        [In] byte[] question,
        int questionLen,
        int maxHops,
        [Out] byte[] answerBuffer,
        int answerCapacity,
        out int answerLen,
        out double confidence);
}
