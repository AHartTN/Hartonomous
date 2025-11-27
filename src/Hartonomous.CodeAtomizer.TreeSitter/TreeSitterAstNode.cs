using Hartonomous.CodeGeneration.Core;

namespace Hartonomous.CodeGeneration.TreeSitter;

/// <summary>
/// IAstNode implementation that wraps TreeSitter nodes.
/// Provides unified interface for AST traversal across all TreeSitter-supported languages.
/// </summary>
public sealed class TreeSitterAstNode : IAstNode
{
    private readonly TreeSitterNode _node;
    private readonly Lazy<IReadOnlyList<IAstNode>> _children;
    private readonly Lazy<IReadOnlyDictionary<string, object>> _metadata;

    public TreeSitterAstNode(TreeSitterNode node)
    {
        _node = node;
        _children = new Lazy<IReadOnlyList<IAstNode>>(() =>
            _node.Children.Select(n => new TreeSitterAstNode(n)).ToList());
        _metadata = new Lazy<IReadOnlyDictionary<string, object>>(ExtractMetadata);
    }

    /// <summary>
    /// Unwrap to get the underlying TreeSitter node for advanced operations.
    /// </summary>
    public TreeSitterNode UnwrapNode() => _node;

    public string NodeType => _node.Type;

    public string FullText => _node.Text;

    public string Text => _node.Text;

    public int StartPosition => (int)_node.StartByte;

    public int EndPosition => (int)_node.EndByte;

    public int Length => (int)(_node.EndByte - _node.StartByte);

    public IAstNode? Parent
    {
        get
        {
            var parent = _node.Parent;
            return parent.IsNull ? null : new TreeSitterAstNode(parent);
        }
    }

    public IReadOnlyList<IAstNode> Children => _children.Value;

    public IReadOnlyDictionary<string, object> Metadata => _metadata.Value;

    private IReadOnlyDictionary<string, object> ExtractMetadata()
    {
        var (startLine, startColumn) = _node.StartPoint;
        var (endLine, endColumn) = _node.EndPoint;

        return new Dictionary<string, object>
        {
            ["TreeSitterType"] = _node.Type,
            ["Symbol"] = _node.Symbol,
            ["StartLine"] = startLine + 1, // TreeSitter uses 0-based, convert to 1-based
            ["StartColumn"] = startColumn + 1,
            ["EndLine"] = endLine + 1,
            ["EndColumn"] = endColumn + 1,
            ["IsNamed"] = _node.IsNamed,
            ["IsError"] = _node.IsError,
            ["HasError"] = _node.HasError,
            ["ChildCount"] = _node.ChildCount,
            ["ByteRange"] = $"{_node.StartByte}-{_node.EndByte}"
        };
    }

    public override string ToString() => _node.ToString();
}
