# TreeSitter Lifetime Bug - Root Cause & Fix

## Problem Description
All integration tests using TreeSitter were crashing with fatal access violation `0xC0000005` when attempting to traverse AST nodes. The crash occurred in native TreeSitter functions:
- `ts_node_type(_node)` 
- `ts_node_child_count(_node)`
- `ts_node_child(_node, index)`

## Root Cause Analysis

### Investigation Process
1. **Initial symptoms**: Tests crashed immediately in `TreeSitterAstNode` constructor or during `Children` enumeration
2. **Failed attempts**: Multiple defensive `try/catch` blocks did not prevent crashes (native exceptions cannot be caught by C#)
3. **Research**: Examined TreeSitter documentation and source code (`TreeSitterWrappers.cs`)
4. **Discovery**: `grep_search` found **ZERO** usages of `using` statements with `TreeSitterTree`

### Root Cause
**TreeSitterTree implements IDisposable but was disposed prematurely during AST traversal.**

#### The Bug (in TreeSitterCodeParser.cs)
```csharp
// BEFORE (BROKEN):
using var tree = parser.Parse(sourceCode);  // Creates tree
var rootNode = tree.RootNode;               // Extracts root node
return new TreeSitterAstNode(rootNode);     // Returns, tree DISPOSED here!
```

#### Why This Crashed
1. `TreeSitterTree` implements `IDisposable` with a finalizer
2. The `using` statement calls `Dispose()` at end of scope
3. `Dispose()` calls native `ts_tree_delete(Handle)` which frees the TSTree*
4. `TreeSitterNode` (stored in `TreeSitterAstNode`) holds a reference to the tree
5. `TreeSitterNode` is a `readonly struct` containing:
   - `TSNode _node` (native struct with pointer to tree)
   - `TreeSitterTree _tree` (reference to managed wrapper)
6. When `using` disposes tree, the native `TSTree*` pointer becomes invalid
7. During AST traversal, accessing `node.NodeType`, `node.Children`, etc. dereferences the invalid pointer
8. Result: Access violation `0xC0000005` (EXCEPTION_ACCESS_VIOLATION)

#### Key Architecture Details
From `TreeSitterWrappers.cs`:
- Line 195: `public sealed class TreeSitterTree : IDisposable`
- Line 240: `Dispose()` calls `ts_tree_delete(Handle)`
- Line 250: Finalizer `~TreeSitterTree()` calls `Dispose()`
- Line 268: `TreeSitterNode` stores `TreeSitterTree _tree` field
- Line 280-378: All node properties access native `TSNode` functions

**The native TSNode struct contains raw pointers to the tree. When the tree is disposed, these pointers become dangling pointers.**

## The Fix

### Solution (in TreeSitterCodeParser.cs)
```csharp
// AFTER (FIXED):
var tree = parser.Parse(sourceCode);        // Creates tree (no 'using')
var rootNode = tree.RootNode;               // Extracts root node (holds tree reference)
return new TreeSitterAstNode(rootNode);     // Returns, tree kept alive by GC
```

**Key changes:**
1. Removed `using` keyword from `tree` variable declaration
2. Tree now stays alive as long as `TreeSitterNode` references exist
3. GC will collect tree only when all nodes are no longer reachable
4. Native `TSTree*` pointer remains valid during traversal

### Files Modified
- `src/Hartonomous.CodeAtomizer.TreeSitter/TreeSitterCodeParser.cs`
  - Line 88: Removed `using` from `Parse()` method
  - Line 105: Removed `using` from `ParseAsync()` method

## Verification

### Before Fix
```
Fatal error. 0xC0000005
at TreeSitterNative.ts_node_type(TSNode)
at TreeSitterNode.get_Type()
at TreeSitterAstNode..ctor(TreeSitterNode node)
```
**All tests crashed immediately**

### After Fix
```
Test summary: total: 5, failed: 1, succeeded: 4, skipped: 0
Test summary: total: 6, failed: 3, succeeded: 3, skipped: 0
```
**Tests run to completion - no crashes!**

Remaining test failures are **logical issues** (missing language grammars, type mismatches), not crashes.

## Impact

### What Was Broken
- ❌ All TreeSitter-based atomization (Python, JavaScript, TypeScript, Go, Rust, Java, Ruby, PHP, C++, etc.)
- ❌ Code generation integration tests
- ❌ Language profile integration tests
- ❌ Any deep AST traversal

### What Is Now Fixed
- ✅ TreeSitter trees stay alive during traversal
- ✅ No more access violations during node access
- ✅ 7 tests now passing (were all crashing before)
- ✅ GC-based lifetime management (automatic cleanup when traversal completes)

## Secondary Issues Discovered

Now that tests run without crashing, they reveal additional problems:

### 1. Python Type Mismatch
```
TreeSitter parsing failed for test.py: Unable to cast object of type 'System.UInt32' to type 'System.Int32'
```
**Cause**: TreeSitter returns `uint` for positions/indices, but code expects `int`
**Impact**: Python atomization falls back to regex parsing
**Status**: Needs separate fix

### 2. C# Grammar Missing
```
Language library not found: tree-sitter-c_sharp.dll
```
**Cause**: C# TreeSitter grammar DLL not deployed to test output directory
**Impact**: C# falls back to Roslyn (which works, but inconsistent)
**Status**: Needs native library deployment fix

### 3. Test Assertions
Some tests fail because:
- Expected atom categories don't match (regex fallback produces different results)
- Expected atom counts don't match (TreeSitter vs regex parsing differences)

**These are expected** given that TreeSitter parsing is falling back to regex due to issues #1 and #2.

## Lessons Learned

1. **Native resource lifetime management is critical** - IDisposable patterns must consider downstream usage
2. **`using` statements are dangerous** when the resource is referenced by return values
3. **Defensive try/catch cannot prevent native crashes** - must fix root cause
4. **Research tools are essential** - Web docs + source examination led to discovery
5. **"Forest for the trees"** - Step back, research properly, find architectural issues

## Next Steps

1. ✅ **COMPLETED**: Fix TreeSitter lifetime bug
2. 🔄 **IN PROGRESS**: Address type mismatch (uint vs int)
3. 🔄 **IN PROGRESS**: Deploy C# TreeSitter grammar
4. 📋 **TODO**: Update tests to handle fallback scenarios
5. 📋 **TODO**: Verify end-to-end code generation flow works

---

**Date**: 2024 (conversation timestamp)  
**Fixed By**: Copilot + User collaboration ("Use your tools and MCP tools to solve this")  
**Verification**: Integration tests now run without crashes
