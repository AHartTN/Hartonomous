namespace Hartonomous.CodeAtomizer.TreeSitter.Core;

/// <summary>
/// Represents a node in an Abstract Syntax Tree (AST).
/// Unified abstraction for TreeSitter nodes across all supported languages.
/// </summary>
public interface IAstNode
{
    /// <summary>
    /// Type of the AST node (e.g., "function_definition", "class_declaration")
    /// Language-specific but normalized for cross-language queries
    /// </summary>
    string NodeType { get; }

    /// <summary>
    /// Full source text including all children and trivia (whitespace, comments)
    /// </summary>
    string FullText { get; }

    /// <summary>
    /// Source text without leading/trailing trivia
    /// </summary>
    string Text { get; }

    /// <summary>
    /// Start position in source file (byte offset)
    /// </summary>
    int StartPosition { get; }

    /// <summary>
    /// End position in source file (byte offset)
    /// </summary>
    int EndPosition { get; }

    /// <summary>
    /// Span length in bytes
    /// </summary>
    int Length { get; }

    /// <summary>
    /// Parent node (null for root)
    /// </summary>
    IAstNode? Parent { get; }

    /// <summary>
    /// Child nodes in document order
    /// </summary>
    IReadOnlyList<IAstNode> Children { get; }

    /// <summary>
    /// Metadata extracted from the node (modifiers, types, line numbers, etc.)
    /// Keys: StartLine, StartColumn, EndLine, EndColumn, IsError, IsNamed, HasError, etc.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }
}
