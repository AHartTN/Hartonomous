# Hartonomous.Native

Native high-performance C++ geometry engine for Hartonomous.

## Overview

This package provides native P/Invoke bindings to the Hartonomous C++ geometry engine, which includes:

- **Hilbert Curve Calculations**: Space-filling curve indexing for content-addressable spatial positioning
- **POINTZM Operations**: 4D coordinate system operations (X=Hilbert, Y=Entropy, Z=Compressibility, M=Connectivity)
- **Spatial Algorithms**: High-performance geometric computations

## Platform Support

- **Windows x64**: `HartonomousNative.dll`
- **Linux x64**: `libHartonomousNative.so`

Native libraries are automatically copied to your output directory when you reference this package.

## Usage

The native library is automatically referenced by `Hartonomous.Core`. You typically don't need to reference this package directly unless you're implementing custom P/Invoke bindings.

```csharp
// P/Invoke declarations in your code
[DllImport("HartonomousNative", CallingConvention = CallingConvention.Cdecl)]
public static extern void YourNativeFunction();
```

## Building from Source

The native library is built using CMake:

```bash
# Configure
cmake -S Hartonomous.Native -B Hartonomous.Native/build -DCMAKE_BUILD_TYPE=Release

# Build
cmake --build Hartonomous.Native/build --config Release
```

## License

Proprietary - Copyright © 2025 Anthony Hart
