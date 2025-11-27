using Hartonomous.CodeGeneration.Core;

namespace Hartonomous.CodeGeneration.TreeSitter;

/// <summary>
/// ICodeParser implementation using TreeSitter for universal multi-language parsing.
/// Supports 50+ languages including Python, JavaScript, TypeScript, Go, Rust, Java, Ruby, PHP, etc.
/// </summary>
public sealed class TreeSitterParser : ICodeParser
{
    private static readonly Dictionary<string, string> _languageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Python
        ["python"] = "python",
        ["py"] = "python",
        
        // JavaScript/TypeScript
        ["javascript"] = "javascript",
        ["js"] = "javascript",
        ["typescript"] = "typescript",
        ["ts"] = "typescript",
        ["tsx"] = "tsx",
        ["jsx"] = "javascript",
        
        // Go
        ["go"] = "go",
        ["golang"] = "go",
        
        // Rust
        ["rust"] = "rust",
        ["rs"] = "rust",
        
        // Java
        ["java"] = "java",
        
        // Ruby
        ["ruby"] = "ruby",
        ["rb"] = "ruby",
        
        // PHP
        ["php"] = "php",
        
        // C/C++
        ["c"] = "c",
        ["cpp"] = "cpp",
        ["c++"] = "cpp",
        ["cxx"] = "cpp",
        
        // C#
        ["csharp"] = "c_sharp",
        ["cs"] = "c_sharp",
        
        // Others
        ["json"] = "json",
        ["yaml"] = "yaml",
        ["toml"] = "toml",
        ["html"] = "html",
        ["css"] = "css",
        ["sql"] = "sql",
        ["bash"] = "bash",
        ["sh"] = "bash",
    };

    private readonly Dictionary<string, TreeSitterLanguage> _loadedLanguages = new();
    private readonly object _lock = new();

    public IReadOnlySet<string> SupportedLanguages => _languageMap.Keys.ToHashSet();

    public IAstNode Parse(string sourceCode, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentException("Source code cannot be null or empty", nameof(sourceCode));

        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language cannot be null or empty", nameof(language));

        var treeSitterLang = GetOrLoadLanguage(language);
        
        using var parser = new TreeSitterNativeParser();
        
        if (!parser.SetLanguage(treeSitterLang))
            throw new InvalidOperationException($"Failed to set language: {language}");

        using var tree = parser.Parse(sourceCode);
        var rootNode = tree.RootNode;

        return new TreeSitterAstNode(rootNode);
    }

    public async Task<IAstNode> ParseAsync(string sourceCode, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentException("Source code cannot be null or empty", nameof(sourceCode));

        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException("Language cannot be null or empty", nameof(language));

        var treeSitterLang = GetOrLoadLanguage(language);
        
        using var parser = new TreeSitterNativeParser();
        
        if (!parser.SetLanguage(treeSitterLang))
            throw new InvalidOperationException($"Failed to set language: {language}");

        using var tree = await parser.ParseAsync(sourceCode, null, cancellationToken);
        var rootNode = tree.RootNode;

        return new TreeSitterAstNode(rootNode);
    }

    public ParserDiagnostics Validate(string sourceCode, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return new ParserDiagnostics(false, new[]
            {
                new ParserDiagnostic(DiagnosticSeverity.Error, "Source code cannot be null or empty", 0, 0)
            });

        if (!_languageMap.ContainsKey(language))
            return new ParserDiagnostics(false, new[]
            {
                new ParserDiagnostic(DiagnosticSeverity.Error, $"Language '{language}' is not supported", 0, 0)
            });

        try
        {
            var ast = Parse(sourceCode, language, cancellationToken);
            var diagnostics = new List<ParserDiagnostic>();

            // Walk tree and find error nodes
            WalkForErrors(ast, diagnostics);

            return new ParserDiagnostics(!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), diagnostics);
        }
        catch (Exception ex)
        {
            return new ParserDiagnostics(false, new[]
            {
                new ParserDiagnostic(DiagnosticSeverity.Error, ex.Message, 0, 0)
            });
        }
    }

    private void WalkForErrors(IAstNode node, List<ParserDiagnostic> diagnostics)
    {
        if (node is TreeSitterAstNode tsNode)
        {
            var rawNode = tsNode.UnwrapNode();
            
            if (rawNode.IsError)
            {
                diagnostics.Add(new ParserDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Syntax error at '{rawNode.Text}'",
                    node.StartPosition,
                    node.EndPosition));
            }
        }

        foreach (var child in node.Children)
        {
            WalkForErrors(child, diagnostics);
        }
    }

    private TreeSitterLanguage GetOrLoadLanguage(string language)
    {
        if (!_languageMap.TryGetValue(language, out var treeSitterName))
            throw new NotSupportedException($"Language '{language}' is not supported. Supported languages: {string.Join(", ", SupportedLanguages)}");

        lock (_lock)
        {
            if (!_loadedLanguages.TryGetValue(treeSitterName, out var loadedLanguage))
            {
                loadedLanguage = TreeSitterLanguage.Load(treeSitterName);
                _loadedLanguages[treeSitterName] = loadedLanguage;
            }

            return loadedLanguage;
        }
    }
}
