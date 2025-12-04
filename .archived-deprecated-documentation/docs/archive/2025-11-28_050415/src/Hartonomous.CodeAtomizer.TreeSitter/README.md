# Hartonomous.CodeGeneration.TreeSitter

**Universal multi-language code parser for .NET using TreeSitter**

Supports 50+ programming languages including Python, JavaScript, TypeScript, Go, Rust, Java, C, C++, C#, and more.

---

## ?? Quick Start

### Prerequisites

**To build from source** (first time only):
- [Rust](https://rustup.rs/) (for building TreeSitter core)
- [Node.js](https://nodejs.org/) (for building language grammars)
- C compiler:
  - Windows: Visual Studio Build Tools
  - Linux: `gcc` (`apt-get install build-essential`)
  - macOS: Xcode Command Line Tools (`xcode-select --install`)

### Installation

```bash
# Clone the repository
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous

# Build native libraries (one-time, takes 5-10 minutes)
pwsh scripts/build-treesitter.ps1

# Build the project
dotnet build
```

The `build-treesitter.ps1` script will:
1. Clone TreeSitter core and compile for your platform
2. Clone and build language grammars
3. Place binaries in `runtimes/{platform}/native/`

---

## ?? Usage

### Parse Python Code

```csharp
using Hartonomous.CodeGeneration.TreeSitter;

var parser = new TreeSitterParser();

var pythonCode = @"
def calculate_sum(numbers):
    total = 0
    for num in numbers:
        total += num
    return total
";

var ast = parser.Parse(pythonCode, "python");

Console.WriteLine($"Root: {ast.NodeType}");
Console.WriteLine($"Children: {ast.Children.Count}");
Console.WriteLine($"Has errors: {ast.Metadata["HasError"]}");
```

### Parse JavaScript/TypeScript

```csharp
var jsCode = "const add = (a, b) => a + b;";
var jsAst = parser.Parse(jsCode, "javascript");

var tsCode = "function hello(): void { console.log('hello'); }";
var tsAst = parser.Parse(tsCode, "typescript");
```

### Traverse AST

```csharp
void WalkTree(IAstNode node, int depth = 0)
{
    var indent = new string(' ', depth * 2);
    Console.WriteLine($"{indent}{node.NodeType}: {node.Text}");
    
    foreach (var child in node.Children)
        WalkTree(child, depth + 1);
}

WalkTree(ast);
```

### Validate Syntax

```csharp
var diagnostics = parser.Validate(sourceCode, "python");

if (!diagnostics.IsValid)
{
    foreach (var diag in diagnostics.Diagnostics)
    {
        Console.WriteLine($"{diag.Severity}: {diag.Message} at {diag.StartPosition}");
    }
}
```

---

## ?? Supported Languages

| Language | Identifier | Status |
|----------|-----------|--------|
| Python | `python` | ? |
| JavaScript | `javascript` | ? |
| TypeScript | `typescript` | ? |
| TSX | `tsx` | ? |
| C# | `csharp` or `c_sharp` | ? |
| Go | `go` | ? |
| Rust | `rust` | ? |
| Java | `java` | ? |
| C | `c` | ? |
| C++ | `cpp` | ? |
| Bash | `bash` | ? |
| JSON | `json` | ? |
| Ruby | `ruby` | ? |
| PHP | `php` | ? |
| HTML | `html` | ? |
| CSS | `css` | ? |

_50+ total languages supported - see [TreeSitter grammars](https://tree-sitter.github.io/tree-sitter/#available-parsers)_

---

## ??? Architecture

```
TreeSitterParser (ICodeParser)
    ?
TreeSitterNativeParser (P/Invoke wrapper)
    ?
tree-sitter.dll/so/dylib (Native C library)
    ?
tree-sitter-python.dll/so/dylib (Language grammars)
```

### Key Components

- **TreeSitterNative.cs** - P/Invoke declarations for TreeSitter C API
- **TreeSitterWrappers.cs** - Safe managed wrappers with IDisposable
- **TreeSitterParser.cs** - ICodeParser implementation
- **TreeSitterAstNode.cs** - IAstNode adapter for unified AST interface

---

## ?? Building Native Libraries

### Automatic Build

When you run `dotnet build` for the first time, the project will automatically:
1. Detect missing native libraries
2. Execute `scripts/build-treesitter.ps1`
3. Build TreeSitter core and language grammars
4. Place binaries in correct `runtimes/` directories

### Manual Build

```powershell
# Build all default languages
.\scripts\build-treesitter.ps1

# Build specific languages only
.\scripts\build-treesitter.ps1 -Languages python,javascript,typescript

# Clean and rebuild
.\scripts\build-treesitter.ps1 -Clean -Force

# Skip core library (already built)
.\scripts\build-treesitter.ps1 -SkipCore
```

### CI/CD Build

GitHub Actions automatically builds for all platforms:

```yaml
# Trigger workflow
git push origin main

# Or manually trigger
gh workflow run build-treesitter.yml
```

Downloads artifacts from Actions:
- `treesitter-win-x64` - Windows binaries
- `treesitter-linux-x64` - Linux binaries
- `treesitter-osx-x64` - macOS Intel binaries
- `treesitter-osx-arm64` - macOS Apple Silicon binaries

---

## ?? NuGet Packaging

Create platform-specific NuGet packages:

```powershell
# Build all platforms (requires CI/CD artifacts)
.\scripts\package-treesitter-for-nuget.ps1 -Version 0.22.6

# Publish to NuGet.org
dotnet nuget push nuget-packages/*.nupkg -s https://api.nuget.org/v3/index.json
```

---

## ?? Testing

```bash
# Run unit tests
dotnet test

# Test specific language
dotnet test --filter "Language=Python"

# Test all parsers
dotnet test --filter "Category=Parser"
```

---

## ?? Troubleshooting

### "tree-sitter.dll not found"

**Cause**: Native libraries not built or missing from output directory.

**Solution**:
```powershell
# Rebuild native libraries
pwsh scripts/build-treesitter.ps1 -Clean -Force

# Verify binaries exist
ls src/Hartonomous.CodeGeneration.TreeSitter/runtimes/win-x64/native/
```

### "Cargo not found"

**Cause**: Rust toolchain not installed.

**Solution**:
```powershell
# Install Rust
winget install Rustlang.Rustup

# Or download from: https://rustup.rs/

# Verify installation
cargo --version
```

### "Node.js not found"

**Cause**: Node.js not installed.

**Solution**:
```powershell
# Install Node.js
winget install OpenJS.NodeJS

# Or download from: https://nodejs.org/

# Verify installation
node --version
npm --version
```

### Build fails on Linux

**Cause**: Missing build tools.

**Solution**:
```bash
# Install build essentials
sudo apt-get update
sudo apt-get install -y build-essential

# Retry build
pwsh scripts/build-treesitter.ps1
```

### Language grammar not available

**Cause**: Grammar not in default build list.

**Solution**:
```powershell
# Add language to build script Languages parameter
.\scripts\build-treesitter.ps1 -Languages python,javascript,ruby,php
```

---

## ?? Performance

| Operation | Time | Notes |
|-----------|------|-------|
| First build | 5-10 min | One-time compilation |
| Parse 1K LOC | < 50 ms | Typical source file |
| Parse 10K LOC | < 500 ms | Large file |
| Parse 100K LOC | < 5 sec | Very large file |

TreeSitter uses incremental parsing - re-parsing after edits is extremely fast (< 10ms for typical changes).

---

## ?? Contributing

1. Add language support:
   - Add grammar repo to `build-treesitter.ps1`
   - Add language identifier to `TreeSitterParser.cs`
   - Add unit tests

2. Improve P/Invoke bindings:
   - Add missing TreeSitter API functions to `TreeSitterNative.cs`
   - Add safe wrappers to `TreeSitterWrappers.cs`

3. Optimize performance:
   - Profile parsing operations
   - Cache language grammars
   - Implement async parsing improvements

---

## ?? License

MIT License - See [LICENSE](../../LICENSE)

TreeSitter is licensed under MIT - See https://github.com/tree-sitter/tree-sitter

Individual language grammars may have different licenses - check each grammar repository.

---

## ?? Links

- [TreeSitter Official Site](https://tree-sitter.github.io/tree-sitter/)
- [TreeSitter GitHub](https://github.com/tree-sitter/tree-sitter)
- [Available Parsers](https://tree-sitter.github.io/tree-sitter/#available-parsers)
- [Hartonomous Documentation](../../docs/VISION.md)

---

## ?? Support

- GitHub Issues: https://github.com/AHartTN/Hartonomous/issues
- Documentation: [docs/VISION.md](../../docs/VISION.md)
- API Reference: [docs/API_REFERENCE.md](../../docs/API_REFERENCE.md)
