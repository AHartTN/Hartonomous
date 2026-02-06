using System.Runtime.InteropServices;
using System.Text;
using Hartonomous.Marshal;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Services;

/// <summary>
/// Wraps Walk Engine + text generation via native C++ interop.
/// All heavy lifting happens in C++.
/// </summary>
public sealed class WalkService : IDisposable
{
    private readonly EngineService _engine;
    private readonly IntPtr _walkHandle;
    private readonly ILogger<WalkService> _logger;
    private bool _disposed;

    public WalkService(EngineService engine, ILogger<WalkService> logger)
    {
        _engine = engine;
        _logger = logger;
        _walkHandle = NativeMethods.WalkCreate(engine.DbHandle);
        if (_walkHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create walk engine: {EngineService.GetLastError()}");
    }

    public unsafe GenerationOutput Generate(string prompt, double temperature = 0.7,
        int maxTokens = 200, double energyDecay = 0.05, string? stopText = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var gp = new GenerateParams
        {
            Temperature = temperature,
            MaxTokens = (nuint)maxTokens,
            EnergyDecay = energyDecay,
            TopP = 1.0,
            N = 1,
        };

        if (stopText != null)
        {
            var bytes = Encoding.UTF8.GetBytes(stopText);
            var len = Math.Min(bytes.Length, 255);
            for (int i = 0; i < len; i++) gp.StopText[i] = bytes[i];
            gp.StopText[len] = 0;
        }

        if (!NativeMethods.Generate(_walkHandle, _engine.DbHandle, prompt, ref gp, out var result))
            throw new InvalidOperationException($"Generation failed: {EngineService.GetLastError()}");

        try
        {
            var text = result.Text != IntPtr.Zero
                ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(result.Text) ?? ""
                : "";

            var finishReason = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(
                (IntPtr)result.FinishReason) ?? "unknown";

            return new GenerationOutput
            {
                Text = text,
                Steps = (int)result.Steps,
                TotalEnergyUsed = result.TotalEnergyUsed,
                FinishReason = finishReason,
            };
        }
        finally
        {
            if (result.Text != IntPtr.Zero)
                NativeMethods.FreeString(result.Text);
        }
    }

    /// <summary>
    /// Streaming generation — calls onFragment for each walk step.
    /// </summary>
    public unsafe GenerationOutput GenerateStream(string prompt, Action<string, int, double> onFragment,
        double temperature = 0.7, int maxTokens = 200, double energyDecay = 0.05, string? stopText = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var gp = new GenerateParams
        {
            Temperature = temperature,
            MaxTokens = (nuint)maxTokens,
            EnergyDecay = energyDecay,
            TopP = 1.0,
            N = 1,
        };

        if (stopText != null)
        {
            var bytes = Encoding.UTF8.GetBytes(stopText);
            var len = Math.Min(bytes.Length, 255);
            for (int i = 0; i < len; i++) gp.StopText[i] = bytes[i];
            gp.StopText[len] = 0;
        }

        // Pin the callback delegate to prevent GC
        NativeMethods.GenerateCallback callback = (fragment, step, energy, _) =>
        {
            onFragment(fragment, (int)step, energy);
            return true;
        };

        if (!NativeMethods.GenerateStream(_walkHandle, _engine.DbHandle, prompt, ref gp,
                callback, IntPtr.Zero, out var result))
            throw new InvalidOperationException($"Streaming generation failed: {EngineService.GetLastError()}");

        try
        {
            var finishReason = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(
                (IntPtr)result.FinishReason) ?? "unknown";

            var text = result.Text != IntPtr.Zero
                ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi(result.Text) ?? ""
                : "";

            return new GenerationOutput
            {
                Text = text,
                Steps = (int)result.Steps,
                TotalEnergyUsed = result.TotalEnergyUsed,
                FinishReason = finishReason,
            };
        }
        finally
        {
            if (result.Text != IntPtr.Zero)
                NativeMethods.FreeString(result.Text);
            GC.KeepAlive(callback);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.WalkDestroy(_walkHandle);
    }
}

public sealed class GenerationOutput
{
    public required string Text { get; init; }
    public int Steps { get; init; }
    public double TotalEnergyUsed { get; init; }
    public required string FinishReason { get; init; }
}
