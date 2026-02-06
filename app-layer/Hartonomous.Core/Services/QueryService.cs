using System.Runtime.InteropServices;
using Hartonomous.Marshal;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Services;

/// <summary>
/// Wraps SemanticQuery — gravitational truth, co-occurrence, Q&A.
/// All heavy lifting in C++.
/// </summary>
public sealed class QueryService : IDisposable
{
    private readonly IntPtr _queryHandle;
    private readonly ILogger<QueryService> _logger;
    private bool _disposed;

    public QueryService(EngineService engine, ILogger<QueryService> logger)
    {
        _logger = logger;
        _queryHandle = NativeMethods.QueryCreate(engine.DbHandle);
        if (_queryHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create query engine: {EngineService.GetLastError()}");
    }

    public List<QueryOutput> FindRelated(string text, int limit = 10)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeMethods.QueryRelated(_queryHandle, text, (nuint)limit, out var resultsPtr, out var count))
            throw new InvalidOperationException($"Query failed: {EngineService.GetLastError()}");

        return MarshalResults(resultsPtr, count);
    }

    public List<QueryOutput> FindGravitationalTruth(string text, double minElo = 1500.0, int limit = 10)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeMethods.QueryTruth(_queryHandle, text, minElo, (nuint)limit, out var resultsPtr, out var count))
            throw new InvalidOperationException($"Truth query failed: {EngineService.GetLastError()}");

        return MarshalResults(resultsPtr, count);
    }

    public QueryOutput? AnswerQuestion(string question)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!NativeMethods.QueryAnswer(_queryHandle, question, out var result))
            throw new InvalidOperationException($"Answer failed: {EngineService.GetLastError()}");

        if (result.Text == IntPtr.Zero) return null;

        var text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(result.Text) ?? "";
        // Single result — text was allocated by C++, but QueryAnswer doesn't use the array path.
        // We need to free it manually.
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.QueryDestroy(_queryHandle);
    }
}

public sealed class QueryOutput
{
    public required string Text { get; init; }
    public double Confidence { get; init; }
}
