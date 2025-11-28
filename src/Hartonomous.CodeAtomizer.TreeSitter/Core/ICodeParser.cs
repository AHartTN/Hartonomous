namespace Hartonomous.CodeAtomizer.TreeSitter.Core;

/// <summary>
/// Parses source code into a language-agnostic AST.
/// TreeSitter implementation supports 50+ languages including Python, JavaScript, TypeScript, Go, Rust, Java, etc.
/// </summary>
public interface ICodeParser
{
    /// <summary>
    /// Languages this parser supports (e.g., "python", "javascript", "go", "rust")
    /// </summary>
    IReadOnlySet<string> SupportedLanguages { get; }

    /// <summary>
    /// Parse source code into AST
    /// </summary>
    IAstNode Parse(string sourceCode, string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse source code asynchronously (for large files)
    /// </summary>
    Task<IAstNode> ParseAsync(string sourceCode, string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate whether source code is syntactically correct
    /// </summary>
    ParserDiagnostics Validate(string sourceCode, string language, CancellationToken cancellationToken = default);
}

/// <summary>
/// Diagnostics from parsing (errors, warnings)
/// </summary>
public sealed record ParserDiagnostics(
    bool IsValid,
    IReadOnlyList<ParserDiagnostic> Diagnostics);

/// <summary>
/// Individual diagnostic message
/// </summary>
public sealed record ParserDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int StartPosition,
    int EndPosition);

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}
