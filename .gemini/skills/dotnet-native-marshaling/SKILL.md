---
name: dotnet-native-marshaling
description: Manage P/Invoke marshaling between C# and C++ Engine. Use when defining NativeBindings, handling unsafe pointers, or bridging libengine.so.
---

# .NET Native Marshaling

## Bridge Architecture
- **Linux**: `libengine.so` | **Windows**: `engine.dll`
- **Convention**: `CallingConvention.Cdecl` for all exports
- **Path resolution**: `NativeLibrary.SetDllImportResolver`

## Type Mappings

| C++ (interop_api.h) | C# (NativeBindings.cs) | Notes |
|:---|:---|:---|
| `uint8_t[16]` | `fixed byte[16]` or `Guid` | BLAKE3 hashes |
| `double[4]` | `fixed double[4]` | S³ coordinates |
| `char*` | `byte*` + UTF8 | String data (unsafe) |
| `void*` / handles | `IntPtr` | Opaque handles |
| `size_t` | `nuint` | Platform-dependent |
| `bool` | `[MarshalAs(UnmanagedType.I1)] bool` | 1-byte boolean |

## Rules
1. **Structs**: Always `[StructLayout(LayoutKind.Sequential)]` with explicit padding to match C++ alignment
2. **Memory ownership**: C++ allocates → C++ frees. Never free C++ memory from C#.
3. **Errors**: Check return `bool`, call `hartonomous_get_last_error()` on failure
4. **Strings**: Always `byte*` + `Marshal.PtrToStringUTF8`, never `string`

## Adding a New Interop Function
1. Declare in `Engine/include/interop_api.h` with `HARTONOMOUS_API`
2. Implement in `Engine/src/interop_api.cpp` with try/catch → `set_last_error()`
3. Declare `[DllImport]` in `app-layer/Hartonomous.Marshal/NativeBindings.cs`
4. Write type-safe C# wrapper with `fixed` blocks
5. Test: verify `Marshal.SizeOf<Struct>()` matches C++ `sizeof()`