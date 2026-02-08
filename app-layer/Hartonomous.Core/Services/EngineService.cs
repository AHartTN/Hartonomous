using System.Runtime.InteropServices;
using Hartonomous.Marshal;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Services;

/// <summary>
/// Manages the lifetime of the native C++ engine connection.
/// Registered as singleton — one connection per application lifetime.
/// </summary>
public sealed class EngineService : NativeHandle
{
    private readonly ILogger<EngineService> _logger;

    public EngineService(string connectionString, ILogger<EngineService> logger)
        : base(NativeMethods.DbCreate(connectionString))
    {
        _logger = logger;
        _logger.LogInformation("Native engine connected");
    }

    public IntPtr DbHandle => RawHandle;

    public bool IsConnected => NativeMethods.DbIsConnected(RawHandle);

    public static string GetLastError() => GetNativeError();

    protected override void DestroyNative(IntPtr handle)
    {
        NativeMethods.DbDestroy(handle);
        _logger?.LogInformation("Native engine disconnected");
    }
}
