---
name: dotnet-native-marshaling
description: Manage P/Invoke marshaling between C# and the C++ Engine. Use when defining NativeBindings, handling unsafe pointers, or mapping HartonomousId (Guid/byte[]) types.
---

# .NET Native Marshaling

This skill governs the complex data transfer and type conversions required for the cross-platform Hartonomous bridge.

## Core Responsibilities

### 1. ID Mapping (The 128-bit Problem)
The project uses 128-bit hashes/IDs throughout.
- **Managed Layer**: Use `Guid` for readability and DB compatibility.
- **Native Layer**: Use 16-byte `fixed byte` arrays or `byte*`.
- **Conversion**:
  ```csharp
  // C# Guid -> Native byte*
  fixed (byte* ptr = guid.ToByteArray()) { ... }
  ```

### 2. Cross-Platform Native Loading
- **Linux**: Resolves to `.so` files. Requires `LD_LIBRARY_PATH` to include the build directory.
- **Windows**: Resolves to `.dll` files. Requires the DLL to be in the application directory or `PATH`.
- **Custom Resolver**: Use `NativeLibrary.SetDllImportResolver` to handle path variations between Linux servers and Windows ingestion clients.

### 3. Unsafe Memory Management
- **Pinned Pointers**: Use `fixed` blocks for ephemeral pointer access.
- **Allocation**: Memory allocated by C++ (e.g., `char*` in `HResearchPlan`) MUST be freed by the corresponding C++ `free` function (e.g., `hartonomous_godel_free_plan`).

## Debugging Workflow
1.  **Entry Point Check**: Verify `EntryPoint` in `DllImport` matches the exact C symbol name.
2.  **Layout Check**: Use `Marshal.SizeOf` to confirm C# struct size matches C++ `sizeof`.
3.  **Exception Mapping**: Catch `AccessViolationException` to identify misaligned struct fields.
4.  **Symbol Visibility**: Ensure `HARTONOMOUS_API` is applied to all C++ exports.