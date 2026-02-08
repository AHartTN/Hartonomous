using System.Runtime.InteropServices;
using Hartonomous.Marshal;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Services;

/// <summary>
/// Wraps SemanticQuery — gravitational truth, co-occurrence, Q&A.
/// All heavy lifting in C++.
/// </summary>
public sealed class QueryService : NativeService
{
    private readonly ILogger<QueryService> _logger;

    public QueryService(EngineService engine, ILogger<QueryService> logger)
        : base(NativeMethods.QueryCreate(engine.DbHandle))
    {
        _logger = logger;
    }

    public List<QueryOutput> FindRelated(string text, int limit = 10)
    {
        if (!NativeMethods.QueryRelated(RawHandle, text, (nuint)limit, out var resultsPtr, out var count))
            throw new InvalidOperationException($"Query failed: {GetNativeError()}");

        return MarshalResults(resultsPtr, count);
    }

    public List<QueryOutput> FindGravitationalTruth(string text, double minElo = 1500.0, int limit = 10)
    {
        if (!NativeMethods.QueryTruth(RawHandle, text, minElo, (nuint)limit, out var resultsPtr, out var count))
            throw new InvalidOperationException($"Truth query failed: {GetNativeError()}");

        return MarshalResults(resultsPtr, count);
    }

    public QueryOutput? AnswerQuestion(string question)
    {
        if (!NativeMethods.QueryAnswer(RawHandle, question, out var result))
            throw new InvalidOperationException($"Answer failed: {GetNativeError()}");

        if (result.Text == IntPtr.Zero) return null;

        var text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(result.Text) ?? "";
        NativeMethods.FreeString(result.Text);
        return new QueryOutput { Text = text, Confidence = result.Confidence };
    }

    private static List<QueryOutput> MarshalResults(IntPtr resultsPtr, nuint count)
    {
        var outputs = new List<QueryOutput>((int)count);
        if (resultsPtr == IntPtr.Zero || count == 0) return outputs;

        try
        {
            var size = System.Runtime.InteropServices.Marshal.SizeOf<QueryResult>();
            for (var i = 0; i < (int)count; i++)
            {
                var r = System.Runtime.InteropServices.Marshal.PtrToStructure<QueryResult>(resultsPtr + i * size);
                var text = r.Text != IntPtr.Zero
                    ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(r.Text) ?? ""
                    : "";
                outputs.Add(new QueryOutput { Text = text, Confidence = r.Confidence });
            }
        }
        finally
        {
            NativeMethods.QueryFreeResults(resultsPtr, count);
        }

        return outputs;
    }

    protected override void DestroyNative(IntPtr handle)
    {
        NativeMethods.QueryDestroy(handle);
    }
}

public sealed class QueryOutput
{
    public required string Text { get; init; }
    public double Confidence { get; init; }
}
