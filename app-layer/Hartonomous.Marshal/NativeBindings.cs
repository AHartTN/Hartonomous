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
}

[StructLayout(LayoutKind.Sequential)]
public struct IngestionStats
{
    public nuint AtomsTotal;
    public nuint AtomsNew;
    public nuint AtomsExisting;
    public nuint CompositionsTotal;
    public nuint CompositionsNew;
    public nuint CompositionsExisting;
    public nuint RelationsTotal;
    public nuint OriginalBytes;
    public nuint StoredBytes;
    public double CompressionRatio;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct KnowledgeGap
{
    public byte* ConceptName;
    public int ReferencesCount;
    public double Confidence;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SubProblem
{
    public ulong NodeId;
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
