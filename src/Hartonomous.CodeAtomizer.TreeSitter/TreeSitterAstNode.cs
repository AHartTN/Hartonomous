using Hartonomous.CodeAtomizer.TreeSitter.Core;

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
        {
            try
            {
                return _node.Children.Select(n => new TreeSitterAstNode(n)).ToList();
            }
            catch
            {
                // TreeSitter native interop can crash, return empty list
                return Array.Empty<IAstNode>();
            }
        });
        _metadata = new Lazy<IReadOnlyDictionary<string, object>>(ExtractMetadata);
    }

    /// <summary>
    /// Unwrap to get the underlying TreeSitter node for advanced operations.
    /// </summary>
    public TreeSitterNode UnwrapNode() => _node;

    public string NodeType => _node.Type;

    public string FullText => _node.Text;

    public string Text => _node.Text;

    public int StartPosition
    {
        get
        {
            try
            {
                return checked((int)_node.StartByte);
            }
            catch (OverflowException)
            {
                throw new ArgumentException($"File size exceeds 2GB limit (StartByte: {_node.StartByte})");
            }
        }
    }

    public int EndPosition
    {
        get
        {
            try
            {
                return checked((int)_node.EndByte);
            }
            catch (OverflowException)
            {
                throw new ArgumentException($"File size exceeds 2GB limit (EndByte: {_node.EndByte})");
            }
        }
    }

    public int Length
    {
        get
        {
            try
            {
                return checked((int)(_node.EndByte - _node.StartByte));
            }
            catch (OverflowException)
            {
                throw new ArgumentException($"Node span exceeds 2GB limit (StartByte: {_node.StartByte}, EndByte: {_node.EndByte})");
            }
        }
    }

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
        try
        {
            var (startLine, startColumn) = _node.StartPoint;
            var (endLine, endColumn) = _node.EndPoint;

            return new Dictionary<string, object>
            {
                ["TreeSitterType"] = _node.Type,
                ["Symbol"] = (int)_node.Symbol, // Convert ushort to int for consistent metadata types
                ["StartLine"] = (int)(startLine + 1), // TreeSitter uses 0-based, convert to 1-based
                ["StartColumn"] = (int)(startColumn + 1),
                ["EndLine"] = (int)(endLine + 1),
                ["EndColumn"] = (int)(endColumn + 1),
                ["IsNamed"] = _node.IsNamed,
                ["IsError"] = _node.IsError,
                ["HasError"] = _node.HasError,
                ["ChildCount"] = (int)_node.ChildCount, // Convert uint to int for consistent metadata types
                ["ByteRange"] = $"{_node.StartByte}-{_node.EndByte}"
            };
        }
        catch
        {
            // TreeSitter native interop can crash, return minimal metadata
            return new Dictionary<string, object>
            {
                ["TreeSitterType"] = "unknown",
                ["Symbol"] = 0,
                ["StartLine"] = 0,
                ["StartColumn"] = 0,
                ["EndLine"] = 0,
                ["EndColumn"] = 0,
                ["IsNamed"] = false,
                ["IsError"] = true,
                ["HasError"] = true,
                ["ChildCount"] = 0,
                ["ByteRange"] = "0-0"
            };
        }
    }

    public override string ToString() => _node.ToString();
}
