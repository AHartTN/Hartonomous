using System.Runtime.InteropServices;
using System.Text;
using Hartonomous.CodeGeneration.TreeSitter.Native;
using static Hartonomous.CodeGeneration.TreeSitter.Native.TreeSitterNative;

namespace Hartonomous.CodeGeneration.TreeSitter;

/// <summary>
/// Managed wrapper for TreeSitter parser.
/// Provides safe disposal and automatic memory management for native resources.
/// </summary>
public sealed unsafe class TreeSitterNativeParser : IDisposable
{
    private TSParser* _parser;
    private bool _disposed;

    public TreeSitterNativeParser()
    {
        _parser = ts_parser_new();
        if (_parser == null)
            throw new OutOfMemoryException("Failed to create TreeSitter parser");
    }

    /// <summary>
    /// Set the language grammar to use for parsing.
    /// </summary>
    public bool SetLanguage(TreeSitterLanguage language)
    {
        ThrowIfDisposed();
        return ts_parser_set_language(_parser, language.Handle);
    }

    /// <summary>
    /// Parse source code and return a syntax tree.
    /// </summary>
    public TreeSitterTree Parse(string sourceCode, TreeSitterTree? oldTree = null)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(sourceCode))
            throw new ArgumentException("Source code cannot be null or empty", nameof(sourceCode));

        // Convert string to UTF-8 bytes for C API
        var sourceBytes = Encoding.UTF8.GetBytes(sourceCode);
        
        fixed (byte* sourcePtr = sourceBytes)
        {
            TSTree* oldTreePtr = oldTree != null ? oldTree.Handle : (TSTree*)IntPtr.Zero;
                
            var tree = ts_parser_parse_string(
                _parser,
                oldTreePtr,
                sourcePtr,
                (uint)sourceBytes.Length);

            if (tree == (TSTree*)IntPtr.Zero)
                throw new InvalidOperationException("Failed to parse source code");

            return new TreeSitterTree(tree, sourceCode);
        }
    }

    /// <summary>
    /// Parse source code asynchronously (performs parsing on thread pool).
    /// </summary>
    public Task<TreeSitterTree> ParseAsync(string sourceCode, TreeSitterTree? oldTree = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Parse(sourceCode, oldTree), cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TreeSitterNativeParser));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_parser != null)
        {
            ts_parser_delete(_parser);
            _parser = null;
        }

        _disposed = true;
    }

    ~TreeSitterNativeParser()
    {
        Dispose();
    }
}

/// <summary>
/// Represents a language grammar for TreeSitter.
/// Language grammars are loaded from native libraries (e.g., tree-sitter-python.dll).
/// </summary>
public sealed unsafe class TreeSitterLanguage
{
    internal TSLanguage* Handle { get; }

    public string Name { get; }

    internal TreeSitterLanguage(TSLanguage* language, string name)
    {
        if (language == null)
            throw new ArgumentNullException(nameof(language));
        
        Handle = language;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Get the ABI version number of the language.
    /// </summary>
    public uint Version => ts_language_version(Handle);

    /// <summary>
    /// Get the number of distinct node types in the language.
    /// </summary>
    public uint SymbolCount => ts_language_symbol_count(Handle);

    /// <summary>
    /// Get the name of a symbol by its ID.
    /// </summary>
    public string GetSymbolName(ushort symbol)
    {
        var namePtr = ts_language_symbol_name(Handle, symbol);
        return Marshal.PtrToStringUTF8((IntPtr)namePtr) ?? string.Empty;
    }

    /// <summary>
    /// Load a language grammar from a native library.
    /// The library must export a function named "tree_sitter_{languageName}".
    /// </summary>
    /// <param name="languageName">Language name (e.g., "python", "javascript", "typescript")</param>
    /// <param name="libraryPath">Optional path to the language library. If null, searches in runtimes folder.</param>
    public static TreeSitterLanguage Load(string languageName, string? libraryPath = null)
    {
        if (string.IsNullOrWhiteSpace(languageName))
            throw new ArgumentException("Language name cannot be null or empty", nameof(languageName));

        // Determine library path if not provided
        if (libraryPath == null)
        {
            var rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64" :
                      RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux-x64" :
                      RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64" :
                      throw new PlatformNotSupportedException("Unsupported platform");

            var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dll" :
                           RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "dylib" : "so";

            var baseDir = AppContext.BaseDirectory;
            libraryPath = Path.Combine(baseDir, "runtimes", rid, "native", $"tree-sitter-{languageName}.{extension}");
        }

        if (!File.Exists(libraryPath))
            throw new FileNotFoundException($"Language library not found: {libraryPath}", libraryPath);

        // Load the native library
        var handle = NativeLibrary.Load(libraryPath);
        
        // Get the language function (convention: tree_sitter_{languageName})
        var functionName = $"tree_sitter_{languageName}";
        var funcPtr = NativeLibrary.GetExport(handle, functionName);
        
        if (funcPtr == IntPtr.Zero)
            throw new EntryPointNotFoundException($"Function '{functionName}' not found in {libraryPath}");

        // Call the function to get the TSLanguage pointer
        var languageFunc = Marshal.GetDelegateForFunctionPointer<LanguageFunction>(funcPtr);
        var languagePtr = languageFunc();

        if (languagePtr == null)
            throw new InvalidOperationException($"Failed to load language: {languageName}");

        return new TreeSitterLanguage(languagePtr, languageName);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate TSLanguage* LanguageFunction();
}

/// <summary>
/// Represents a parsed syntax tree.
/// Must be disposed to free native memory.
/// </summary>
public sealed unsafe class TreeSitterTree : IDisposable
{
    internal TSTree* Handle { get; private set; }
    
    public string SourceCode { get; }
    
    private bool _disposed;

    internal TreeSitterTree(TSTree* tree, string sourceCode)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));
        
        Handle = tree;
        SourceCode = sourceCode ?? throw new ArgumentNullException(nameof(sourceCode));
    }

    /// <summary>
    /// Get the root node of the syntax tree.
    /// </summary>
    public TreeSitterNode RootNode
    {
        get
        {
            ThrowIfDisposed();
            var node = ts_tree_root_node(Handle);
            return new TreeSitterNode(node, this);
        }
    }

    /// <summary>
    /// Get the language that was used to parse this tree.
    /// </summary>
    internal TSLanguage* Language
    {
        get
        {
            ThrowIfDisposed();
            return ts_tree_language(Handle);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TreeSitterTree));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (Handle != null)
        {
            ts_tree_delete(Handle);
            Handle = null;
        }

        _disposed = true;
    }

    ~TreeSitterTree()
    {
        Dispose();
    }
}

/// <summary>
/// Represents a single node in a TreeSitter syntax tree.
/// This is a lightweight struct that references the tree.
/// </summary>
public readonly unsafe struct TreeSitterNode
{
    private readonly TSNode _node;
    private readonly TreeSitterTree _tree;

    internal TreeSitterNode(TSNode node, TreeSitterTree tree)
    {
        _node = node;
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    }

    /// <summary>
    /// Check if this node is null (represents no node).
    /// </summary>
    public bool IsNull => ts_node_is_null(_node);

    /// <summary>
    /// Get the node's type as a string (e.g., "function_definition", "identifier").
    /// </summary>
    public string Type
    {
        get
        {
            var typePtr = ts_node_type(_node);
            return Marshal.PtrToStringUTF8((IntPtr)typePtr) ?? string.Empty;
        }
    }

    /// <summary>
    /// Get the node's type as a numerical symbol ID.
    /// </summary>
    public ushort Symbol => ts_node_symbol(_node);

    /// <summary>
    /// Get the byte offset where this node starts in the source.
    /// </summary>
    public uint StartByte => ts_node_start_byte(_node);

    /// <summary>
    /// Get the byte offset where this node ends in the source.
    /// </summary>
    public uint EndByte => ts_node_end_byte(_node);

    /// <summary>
    /// Get the line/column where this node starts.
    /// </summary>
    public (uint Line, uint Column) StartPoint
    {
        get
        {
            var point = ts_node_start_point(_node);
            return (point.row, point.column);
        }
    }

    /// <summary>
    /// Get the line/column where this node ends.
    /// </summary>
    public (uint Line, uint Column) EndPoint
    {
        get
        {
            var point = ts_node_end_point(_node);
            return (point.row, point.column);
        }
    }

    /// <summary>
    /// Get the text of this node from the source code.
    /// </summary>
    public string Text
    {
        get
        {
            var start = (int)StartByte;
            var length = (int)(EndByte - StartByte);
            
            if (start < 0 || start >= _tree.SourceCode.Length)
                return string.Empty;
            
            if (start + length > _tree.SourceCode.Length)
                length = _tree.SourceCode.Length - start;
            
            return _tree.SourceCode.Substring(start, length);
        }
    }

    /// <summary>
    /// Get the number of children this node has.
    /// </summary>
    public uint ChildCount => ts_node_child_count(_node);

    /// <summary>
    /// Get a child node by index.
    /// </summary>
    public TreeSitterNode GetChild(uint index)
    {
        var child = ts_node_child(_node, index);
        return new TreeSitterNode(child, _tree);
    }

    /// <summary>
    /// Get all children of this node.
    /// </summary>
    public IEnumerable<TreeSitterNode> Children
    {
        get
        {
            var count = ChildCount;
            for (uint i = 0; i < count; i++)
            {
                yield return GetChild(i);
            }
        }
    }

    /// <summary>
    /// Get the parent of this node.
    /// </summary>
    public TreeSitterNode Parent => new TreeSitterNode(ts_node_parent(_node), _tree);

    /// <summary>
    /// Check if this is a named node (vs anonymous like punctuation).
    /// </summary>
    public bool IsNamed => ts_node_is_named(_node);

    /// <summary>
    /// Check if this node represents a syntax error.
    /// </summary>
    public bool IsError => ts_node_is_error(_node);

    /// <summary>
    /// Check if this node contains any syntax errors.
    /// </summary>
    public bool HasError => ts_node_has_error(_node);

    public override string ToString() => $"{Type}: {Text.Substring(0, Math.Min(50, Text.Length))}";
}
