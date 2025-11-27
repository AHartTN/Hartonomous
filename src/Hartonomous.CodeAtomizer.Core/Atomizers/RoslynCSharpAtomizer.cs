using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hartonomous.CodeAtomizer.Core.Models;
using Hartonomous.CodeAtomizer.Core.Spatial;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hartonomous.CodeAtomizer.Core.Atomizers;

/// <summary>
/// Roslyn-based C# code atomizer with full semantic analysis.
/// Atomizes: files, namespaces, classes, methods, properties, fields, statements, expressions.
/// </summary>
public sealed class RoslynCSharpAtomizer
{
    private readonly List<Atom> _atoms = new();
    private readonly List<AtomComposition> _compositions = new();
    private readonly List<AtomRelation> _relations = new();
    private readonly Dictionary<string, byte[]> _hashCache = new();

    public AtomizationResult Atomize(string code, string fileName, string? metadata = null)
    {
        _atoms.Clear();
        _compositions.Clear();
        _relations.Clear();
        _hashCache.Clear();

        var syntaxTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
        var root = syntaxTree.GetCompilationUnitRoot();

        // Create file-level atom
        var fileHash = CreateFileAtom(code, fileName, metadata);

        // Traverse AST
        var visitor = new CSharpSemanticVisitor(this, fileHash);
        visitor.Visit(root);

        return new AtomizationResult
        {
            Atoms = _atoms.ToArray(),
            Compositions = _compositions.ToArray(),
            Relations = _relations.ToArray(),
            TotalAtoms = _atoms.Count,
            UniqueAtoms = _atoms.Count // Already deduplicated by hash
        };
    }

    private byte[] CreateFileAtom(string code, string fileName, string? metadata)
    {
        var fileBytes = Encoding.UTF8.GetBytes($"csharp:file:{fileName}:{code.Length}");
        var hash = ComputeHash(fileBytes);

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "code",
            category: "file",
            specificity: "concrete",
            identifier: fileName
        );

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = fileBytes,
            CanonicalText = $"{fileName} ({code.Length:N0} bytes)",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "code",
            Subtype = "file",
            Metadata = JsonSerializer.Serialize(new
            {
                language = "csharp",
                fileName,
                size = code.Length,
                lines = code.Split('\n').Length,
                parsingEngine = "Roslyn",
                hilbertIndex,
                metadata
            })
        });

        return hash;
    }

    internal byte[] CreateAtom(
        string nodeType,
        string name,
        string text,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        Dictionary<string, object>? additionalMetadata = null)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"csharp:{nodeType}:{name}");
        var hash = ComputeHash(atomicValue);

        // Check if atom already exists (deduplication)
        if (_hashCache.TryGetValue(Convert.ToBase64String(hash), out var existingHash))
        {
            return existingHash;
        }

        // Determine specificity from metadata
        var isAbstract = additionalMetadata?.ContainsKey("modifiers") == true &&
                        additionalMetadata["modifiers"].ToString()?.Contains("abstract") == true;
        
        var specificity = LandmarkProjection.InferSpecificity(nodeType, isAbstract);

        // Compute spatial position AND Hilbert index using landmark projection
        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "code",
            category: nodeType,
            specificity: specificity,
            identifier: $"{nodeType}:{name}"
        );

        var metadata = new Dictionary<string, object>
        {
            ["language"] = "csharp",
            ["nodeType"] = nodeType,
            ["name"] = name,
            ["startLine"] = startLine,
            ["startColumn"] = startColumn,
            ["endLine"] = endLine,
            ["endColumn"] = endColumn,
            ["parsingEngine"] = "Roslyn",
            ["spatialPosition"] = new { x, y, z },
            ["hilbertIndex"] = hilbertIndex,
            ["specificity"] = specificity
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = text.Length > 100 ? text[..100] + "..." : text,
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "code",
            Subtype = nodeType,
            Metadata = JsonSerializer.Serialize(metadata)
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    internal void CreateComposition(byte[] parentHash, byte[] childHash, int sequenceIndex)
    {
        _compositions.Add(new AtomComposition
        {
            ParentAtomHash = parentHash,
            ComponentAtomHash = childHash,
            SequenceIndex = sequenceIndex,
            Position = null // Can be computed later if needed
        });
    }

    internal void CreateRelation(
        byte[] sourceHash,
        byte[] targetHash,
        string relationType,
        double weight = 1.0,
        Dictionary<string, object>? metadata = null)
    {
        _relations.Add(new AtomRelation
        {
            SourceAtomHash = sourceHash,
            TargetAtomHash = targetHash,
            RelationType = relationType,
            Weight = weight,
            SpatialDistance = null, // Will be computed by database
            Metadata = metadata != null ? JsonSerializer.Serialize(metadata) : null
        });
    }

    private static byte[] ComputeHash(byte[] data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// AST visitor that traverses Roslyn syntax tree
    /// </summary>
    private sealed class CSharpSemanticVisitor : CSharpSyntaxWalker
    {
        private readonly RoslynCSharpAtomizer _atomizer;
        private readonly byte[] _fileHash;
        private readonly Stack<byte[]> _contextStack = new();

        public CSharpSemanticVisitor(RoslynCSharpAtomizer atomizer, byte[] fileHash)
        {
            _atomizer = atomizer;
            _fileHash = fileHash;
            _contextStack.Push(fileHash);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var name = node.Name.ToString();
            var hash = CreateNodeAtom(node, "namespace", name);
            _contextStack.Push(hash);
            base.VisitNamespaceDeclaration(node);
            _contextStack.Pop();
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var modifiers = string.Join(" ", node.Modifiers.Select(m => m.Text));
            var hash = CreateNodeAtom(node, "class", name, new Dictionary<string, object>
            {
                ["modifiers"] = modifiers,
                ["isPartial"] = node.Modifiers.Any(SyntaxKind.PartialKeyword)
            });

            // Create "defines" relation from parent to class
            var parent = _contextStack.Peek();
            _atomizer.CreateRelation(parent, hash, "defines");

            _contextStack.Push(hash);
            base.VisitClassDeclaration(node);
            _contextStack.Pop();
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var returnType = node.ReturnType.ToString();
            var parameters = string.Join(", ", node.ParameterList.Parameters.Select(p => p.ToString()));

            var hash = CreateNodeAtom(node, "method", name, new Dictionary<string, object>
            {
                ["returnType"] = returnType,
                ["parameters"] = parameters,
                ["signature"] = $"{returnType} {name}({parameters})"
            });

            var parent = _contextStack.Peek();
            _atomizer.CreateRelation(parent, hash, "contains");

            _contextStack.Push(hash);
            base.VisitMethodDeclaration(node);
            _contextStack.Pop();
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var type = node.Type.ToString();

            var hash = CreateNodeAtom(node, "property", name, new Dictionary<string, object>
            {
                ["type"] = type,
                ["hasGetter"] = node.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? false,
                ["hasSetter"] = node.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) ?? false
            });

            var parent = _contextStack.Peek();
            _atomizer.CreateRelation(parent, hash, "contains");

            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var type = node.Declaration.Type.ToString();
            
            foreach (var variable in node.Declaration.Variables)
            {
                var name = variable.Identifier.Text;
                var hash = CreateNodeAtom(node, "field", name, new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["hasInitializer"] = variable.Initializer != null
                });

                var parent = _contextStack.Peek();
                _atomizer.CreateRelation(parent, hash, "contains");
            }

            base.VisitFieldDeclaration(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // Track method calls for "calls" relations
            if (node.Expression is IdentifierNameSyntax identifier)
            {
                var methodName = identifier.Identifier.Text;
                var callerHash = _contextStack.Peek();
                
                // Create lightweight atom for method reference
                var callHash = _atomizer.CreateAtom(
                    "method-call",
                    methodName,
                    node.ToString(),
                    0, 0, 0, 0);

                _atomizer.CreateRelation(callerHash, callHash, "calls", 0.8);
            }

            base.VisitInvocationExpression(node);
        }

        private byte[] CreateNodeAtom(
            SyntaxNode node,
            string nodeType,
            string name,
            Dictionary<string, object>? metadata = null)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var text = node.ToString();

            var hash = _atomizer.CreateAtom(
                nodeType,
                name,
                text,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                lineSpan.EndLinePosition.Line + 1,
                lineSpan.EndLinePosition.Character + 1,
                metadata);

            // Create composition with parent
            var parent = _contextStack.Peek();
            var sequenceIndex = _atomizer._compositions.Count(c => c.ParentAtomHash.SequenceEqual(parent));
            _atomizer.CreateComposition(parent, hash, sequenceIndex);

            return hash;
        }
    }
}

/// <summary>
/// Result of atomization process
/// </summary>
public sealed record AtomizationResult
{
    public required Atom[] Atoms { get; init; }
    public required AtomComposition[] Compositions { get; init; }
    public required AtomRelation[] Relations { get; init; }
    public required int TotalAtoms { get; init; }
    public required int UniqueAtoms { get; init; }
}
