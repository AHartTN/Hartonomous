using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Hartonomous.Marshal;

/// <summary>
/// Custom DllImportResolver for Hartonomous native library following Microsoft best practices.
/// </summary>
/// <remarks>
/// References:
/// - https://learn.microsoft.com/en-us/dotnet/standard/native-interop/native-library-loading
/// - https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativelibrary.setdllimportresolver
/// </remarks>
internal static class NativeLibraryResolver
{
    private static bool _resolverRegistered = false;
    private static readonly object _lock = new();

    /// <summary>
    /// Registers the custom DLL import resolver for this assembly.
    /// Must be called before any P/Invoke calls.
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_resolverRegistered) return;
            
            NativeLibrary.SetDllImportResolver(
                Assembly.GetExecutingAssembly(),
                DllImportResolver);
            
            _resolverRegistered = true;
        }
    }

    /// <summary>
    /// Custom resolver that locates Hartonomous.Native library based on RID.
    /// </summary>
    /// <remarks>
    /// Search order:
    /// 1. Same directory as Marshal.dll (ProjectReference scenario)
    /// 2. runtimes/{rid}/native/ subdirectory (NuGet package scenario)
    /// 3. Fallback to default resolution (lets runtime try its probing paths)
    /// </remarks>
    private static IntPtr DllImportResolver(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        // Only handle our native library
        if (!libraryName.Contains("Hartonomous.Native"))
        {
            return IntPtr.Zero; // Fall back to default resolution
        }

        // Determine the RID-specific library name
        string nativeLibraryName = GetNativeLibraryName();
        
        // Get the directory where Marshal.dll is located
        string assemblyDirectory = Path.GetDirectoryName(assembly.Location)!;
        
        // Search paths in priority order
        string[] searchPaths = new[]
        {
            // 1. Same directory (ProjectReference scenario - copy-to-output)
            Path.Combine(assemblyDirectory, nativeLibraryName),
            
            // 2. runtimes/{rid}/native/ subdirectory (NuGet package scenario)
            Path.Combine(assemblyDirectory, "runtimes", GetRuntimeIdentifier(), "native", nativeLibraryName),
            
            // 3. Just the file name (let runtime probe default paths)
            nativeLibraryName
        };

        foreach (string path in searchPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    return NativeLibrary.Load(path, assembly, searchPath);
                }
                catch
                {
                    // Continue to next path
                }
            }
        }

        // Last resort: try to load by name only (runtime will probe)
        try
        {
            return NativeLibrary.Load(nativeLibraryName, assembly, searchPath);
        }
        catch
        {
            // Return IntPtr.Zero to let runtime try its default resolution
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Gets the platform-specific native library name.
    /// </summary>
    private static string GetNativeLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Hartonomous.Native.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "libHartonomous.Native.so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libHartonomous.Native.dylib";
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"Unsupported platform: {RuntimeInformation.OSDescription}");
        }
    }

    /// <summary>
    /// Gets the runtime identifier (RID) for the current platform.
    /// </summary>
    private static string GetRuntimeIdentifier()
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                  : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                  : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
                  : throw new PlatformNotSupportedException();

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
        };

        return $"{os}-{arch}";
    }
}
