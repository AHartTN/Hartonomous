using System;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

public static unsafe class NativeMethods
{
    private const string LibName = "engine"; // Resolves to engine.dll or libengine.so

    // =========================================================================
    //  Error Handling
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_get_last_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetLastError();

    // =========================================================================
    //  Database Connection
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_db_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr DbCreate([MarshalAs(UnmanagedType.LPStr)] string connectionString);

    [DllImport(LibName, EntryPoint = "hartonomous_db_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void DbDestroy(IntPtr handle);

    [DllImport(LibName, EntryPoint = "hartonomous_db_is_connected", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool DbIsConnected(IntPtr handle);

    // =========================================================================
    //  Core Primitives (Hashing & Projection)
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_get_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GetVersion();

    [DllImport(LibName, EntryPoint = "hartonomous_blake3_hash", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Blake3Hash(byte* data, nuint len, byte* out16b);

    [DllImport(LibName, EntryPoint = "hartonomous_blake3_hash_codepoint", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Blake3HashCodepoint(uint codepoint, byte* out16b);

    [DllImport(LibName, EntryPoint = "hartonomous_codepoint_to_s3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CodepointToS3(uint codepoint, double* out4d);

    [DllImport(LibName, EntryPoint = "hartonomous_s3_to_hilbert", CallingConvention = CallingConvention.Cdecl)]
    public static extern void S3ToHilbert(double* in4d, uint entityType, ulong* outHi, ulong* outLo);

    [DllImport(LibName, EntryPoint = "hartonomous_s3_compute_centroid", CallingConvention = CallingConvention.Cdecl)]
    public static extern void S3ComputeCentroid(double* points4d, nuint count, double* out4d);

    // =========================================================================
    //  Ingestion Service
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_ingester_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr IngesterCreate(IntPtr dbHandle);

    [DllImport(LibName, EntryPoint = "hartonomous_ingester_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void IngesterDestroy(IntPtr handle);

    [DllImport(LibName, EntryPoint = "hartonomous_ingest_text", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool IngestText(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string text, out IngestionStats stats);

    [DllImport(LibName, EntryPoint = "hartonomous_ingest_file", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool IngestFile(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string filePath, out IngestionStats stats);

    // =========================================================================
    //  Godel Engine
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_godel_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr GodelCreate(IntPtr dbHandle);

    [DllImport(LibName, EntryPoint = "hartonomous_godel_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void GodelDestroy(IntPtr handle);

    [DllImport(LibName, EntryPoint = "hartonomous_godel_analyze", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GodelAnalyze(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string problem, out ResearchPlan plan);

    [DllImport(LibName, EntryPoint = "hartonomous_godel_free_plan", CallingConvention = CallingConvention.Cdecl)]
    public static extern void GodelFreePlan(ref ResearchPlan plan);

    // =========================================================================
    //  Walk Engine
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_walk_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr WalkCreate(IntPtr dbHandle);

    [DllImport(LibName, EntryPoint = "hartonomous_walk_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void WalkDestroy(IntPtr handle);

    [DllImport(LibName, EntryPoint = "hartonomous_walk_init", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool WalkInit(IntPtr handle, byte* startId, double initialEnergy, out WalkState state);

    [DllImport(LibName, EntryPoint = "hartonomous_walk_step", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool WalkStep(IntPtr handle, ref WalkState state, ref WalkParameters params_, out WalkStepResult result);

    [DllImport(LibName, EntryPoint = "hartonomous_walk_set_goal", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool WalkSetGoal(IntPtr handle, ref WalkState state, byte* goalId);

    // =========================================================================
    //  Text Generation
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_generate", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Generate(IntPtr walkHandle, IntPtr dbHandle,
        [MarshalAs(UnmanagedType.LPStr)] string prompt, ref GenerateParams params_,
        out GenerateResult result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool GenerateCallback(
        [MarshalAs(UnmanagedType.LPStr)] string fragment,
        nuint step, double energyRemaining, IntPtr userData);

    [DllImport(LibName, EntryPoint = "hartonomous_generate_stream", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GenerateStream(IntPtr walkHandle, IntPtr dbHandle,
        [MarshalAs(UnmanagedType.LPStr)] string prompt, ref GenerateParams params_,
        GenerateCallback callback, IntPtr userData, out GenerateResult result);

    [DllImport(LibName, EntryPoint = "hartonomous_free_string", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FreeString(IntPtr str);

    // =========================================================================
    //  Semantic Query
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_query_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr QueryCreate(IntPtr dbHandle);

    [DllImport(LibName, EntryPoint = "hartonomous_query_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void QueryDestroy(IntPtr handle);

    [DllImport(LibName, EntryPoint = "hartonomous_query_related", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool QueryRelated(IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string text, nuint limit,
        out IntPtr results, out nuint count);

    [DllImport(LibName, EntryPoint = "hartonomous_query_truth", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool QueryTruth(IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string text, double minElo, nuint limit,
        out IntPtr results, out nuint count);

    [DllImport(LibName, EntryPoint = "hartonomous_query_answer", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool QueryAnswer(IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string question, out QueryResult result);

    [DllImport(LibName, EntryPoint = "hartonomous_query_free_results", CallingConvention = CallingConvention.Cdecl)]
    public static extern void QueryFreeResults(IntPtr results, nuint count);

    // =========================================================================
    //  Composition Lookup
    // =========================================================================

    [DllImport(LibName, EntryPoint = "hartonomous_composition_text", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr CompositionText(IntPtr dbHandle, byte* hash16b);

    [DllImport(LibName, EntryPoint = "hartonomous_composition_position", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CompositionPosition(IntPtr dbHandle, byte* hash16b, double* out4d);
}

[StructLayout(LayoutKind.Sequential)]
public struct IngestionStats
{
    public nuint AtomsTotal;
    public nuint AtomsNew;
    public nuint CompositionsTotal;
    public nuint CompositionsNew;
    public nuint RelationsTotal;
    public nuint RelationsNew;
    public nuint EvidenceCount;
    public nuint OriginalBytes;
    public nuint StoredBytes;
    public double CompressionRatio;
    public nuint NgramsExtracted;
    public nuint NgramsSignificant;
    public nuint CooccurrencesFound;
    public nuint CooccurrencesSignificant;
}

[StructLayout(LayoutKind.Sequential)]
public struct WalkParameters
{
    public double WModel;
    public double WText;
    public double WRel;
    public double WGeo;
    public double WHilbert;
    public double WRepeat;
    public double WNovelty;
    public double GoalAttraction;
    public double WEnergy;
    public double BaseTemp;
    public double EnergyAlpha;
    public double EnergyDecay;
    public nuint ContextWindow;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WalkState
{
    public fixed byte CurrentComposition[16];
    public fixed double CurrentPosition[4];
    public double CurrentEnergy;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct WalkStepResult
{
    public fixed byte NextComposition[16];
    public double Probability;
    public double EnergyRemaining;
    [MarshalAs(UnmanagedType.I1)]
    public bool Terminated;
    public fixed byte Reason[256];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct KnowledgeGap
{
    public byte* ConceptName;
    public int ReferencesCount;
    public double Confidence;
}

public enum EntityType : int
{
    Composition = 0,
    Atom = 1
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SubProblem
{
    public fixed byte NodeId[16];
    public byte* Description;
    public int Difficulty;
    [MarshalAs(UnmanagedType.I1)]
    public bool IsSolvable;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ResearchPlan
{
    public byte* OriginalProblem;
    public SubProblem* SubProblems;
    public nuint SubProblemsCount;
    public KnowledgeGap* KnowledgeGaps;
    public nuint KnowledgeGapsCount;
    public int TotalSteps;
    public int SolvableSteps;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct GenerateParams
{
    public double Temperature;
    public nuint MaxTokens;
    public double EnergyDecay;
    public double TopP;
    public nuint N;
    public fixed byte StopText[256];
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct GenerateResult
{
    public IntPtr Text;        // Allocated by C++, free with FreeString
    public nuint Steps;
    public double TotalEnergyUsed;
    public fixed byte FinishReason[64];
}

[StructLayout(LayoutKind.Sequential)]
public struct QueryResult
{
    public IntPtr Text;         // Allocated by C++, free with QueryFreeResults
    public double Confidence;
}
