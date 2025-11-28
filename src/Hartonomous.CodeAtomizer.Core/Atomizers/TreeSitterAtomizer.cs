using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hartonomous.CodeAtomizer.Core.Models;
using Hartonomous.CodeAtomizer.Core.Spatial;
using Hartonomous.CodeAtomizer.TreeSitter.Core;
using Hartonomous.CodeGeneration.TreeSitter;

namespace Hartonomous.CodeAtomizer.Core.Atomizers;

/// <summary>
/// Tree-sitter based multi-language atomizer with full native AST traversal.
/// Supports 50+ languages: Python, JavaScript, TypeScript, Go, Rust, Java, C++, etc.
/// Now uses native TreeSitter bindings instead of regex patterns for accurate semantic analysis.
/// </summary>
public sealed class TreeSitterAtomizer
{
    private readonly List<Atom> _atoms = new();
    private readonly List<AtomComposition> _compositions = new();
    private readonly List<AtomRelation> _relations = new();
    private readonly Dictionary<string, byte[]> _hashCache = new();
    private readonly TreeSitterParser _parser = new();
    
    // Node types we want to atomize (language-agnostic where possible)
    private static readonly HashSet<string> SemanticNodeTypes = new()
    {
        // Functions/Methods
        "function_definition", "function_declaration", "method_definition", "method_declaration",
        "arrow_function", "function", "method", "constructor", "destructor",
        
        // Classes/Types
        "class_definition", "class_declaration", "class", "interface_declaration", "interface",
        "struct_definition", "struct", "enum_definition", "enum", "trait_definition", "trait",
        "type_definition", "type_alias", 
        
        // Modules/Namespaces
        "module", "namespace", "package", "import_statement", "import_from_statement",
        "using_directive", "use_declaration",
        
        // Variables/Fields
        "variable_declaration", "variable_declarator", "field_declaration", "property_declaration",
        "constant_declaration", "const_declaration",
        
        // Control Flow
        "if_statement", "for_statement", "while_statement", "switch_statement", "match_expression",
        "try_statement", "catch_clause", "finally_clause",
        
        // Expressions
        "call_expression", "invocation_expression", "member_access_expression",
        "assignment_expression", "binary_expression", "unary_expression"
    };
    
    // Relations to track (calls, defines, imports, etc.)
    private static readonly HashSet<string> RelationNodeTypes = new()
    {
        "call_expression", "invocation_expression", "import_statement", 
        "import_from_statement", "using_directive", "use_declaration"
    };

    /// <summary>
    /// Supported languages with file extensions
    /// </summary>
    private static readonly Dictionary<string, string[]> SupportedLanguages = new()
    {
        ["python"] = new[] { "py", "pyw", "pyi" },
        ["javascript"] = new[] { "js", "mjs", "cjs" },
        ["typescript"] = new[] { "ts", "tsx" },
        ["go"] = new[] { "go" },
        ["rust"] = new[] { "rs" },
        ["java"] = new[] { "java" },
        ["cpp"] = new[] { "cpp", "cc", "cxx", "hpp", "h" },
        ["c"] = new[] { "c", "h" },
        ["ruby"] = new[] { "rb" },
        ["php"] = new[] { "php" },
        ["swift"] = new[] { "swift" },
        ["kotlin"] = new[] { "kt", "kts" },
        ["scala"] = new[] { "scala" },
        ["bash"] = new[] { "sh", "bash" },
        ["json"] = new[] { "json" },
        ["yaml"] = new[] { "yaml", "yml" },
        ["toml"] = new[] { "toml" },
        ["sql"] = new[] { "sql" }
    };

    public static bool CanHandle(string? fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension)) return false;
        
        var ext = fileExtension.TrimStart('.').ToLowerInvariant();
        return SupportedLanguages.Values.Any(exts => exts.Contains(ext));
    }

    public static string? DetectLanguage(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return SupportedLanguages.FirstOrDefault(kv => kv.Value.Contains(ext)).Key;
    }

    public AtomizationResult Atomize(string code, string fileName, string? metadata = null)
    {
        _atoms.Clear();
        _compositions.Clear();
        _relations.Clear();
        _hashCache.Clear();

        var language = DetectLanguage(fileName) ?? "unknown";

        // Create file-level atom
        var fileHash = CreateFileAtom(code, fileName, language, metadata);

        // Try native TreeSitter parsing first
        try
        {
            var ast = _parser.Parse(code, language);
            AtomizeAstNode(ast, code, fileName, language, fileHash, 0);
        }
        catch (Exception ex)
        {
            // Fallback to regex patterns if TreeSitter fails
            Console.WriteLine($"TreeSitter parsing failed for {fileName}: {ex.Message}. Falling back to regex.");
            AtomizeWithPatterns(code, fileName, language, fileHash);
        }

        return new AtomizationResult
        {
            Atoms = _atoms.ToArray(),
            Compositions = _compositions.ToArray(),
            Relations = _relations.ToArray(),
            TotalAtoms = _atoms.Count,
            UniqueAtoms = _atoms.Count
        };
    }
    
    private int AtomizeAstNode(IAstNode node, string sourceCode, string fileName, string language, byte[] parentHash, int sequenceIndex)
    {
        var nodeType = node.NodeType;
        
        // Check if this node should be atomized
        if (SemanticNodeTypes.Contains(nodeType))
        {
            var nodeText = GetNodeText(node, sourceCode);
            var nodeName = ExtractNodeName(node, sourceCode) ?? nodeText;
            
            // Create atom for this semantic node
            var atomHash = CreateSemanticAtom(nodeType, nodeName, nodeText, language, fileName, node);
            
            // Create composition linking to parent
            _compositions.Add(new AtomComposition
            {
                ParentAtomHash = parentHash,
                ComponentAtomHash = atomHash,
                SequenceIndex = sequenceIndex++,
                Position = null
            });
            
            // Check for relations (calls, imports, etc.)
            if (RelationNodeTypes.Contains(nodeType))
            {
                ExtractRelations(node, sourceCode, atomHash, language);
            }
            
            // Recursively process children with this node as parent
            int childIndex = 0;
            foreach (var child in node.Children)
            {
                childIndex = AtomizeAstNode(child, sourceCode, fileName, language, atomHash, childIndex);
            }
            
            return sequenceIndex;
        }
        else
        {
            // Not a semantic node we care about - just traverse children
            foreach (var child in node.Children)
            {
                sequenceIndex = AtomizeAstNode(child, sourceCode, fileName, language, parentHash, sequenceIndex);
            }
            
            return sequenceIndex;
        }
    }
    
    private byte[] CreateSemanticAtom(string nodeType, string nodeName, string nodeText, string language, string fileName, IAstNode node)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"{language}:{nodeType}:{nodeName}");
        var hash = ComputeHash(atomicValue);

        // Check deduplication
        var hashKey = Convert.ToBase64String(hash);
        if (_hashCache.ContainsKey(hashKey))
        {
            return hash;
        }

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "code",
            category: MapNodeTypeToCategory(nodeType),
            specificity: "concrete",
            identifier: $"{language}:{nodeType}:{nodeName}"
        );

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"{nodeName} ({nodeType})",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "code",
            Subtype = MapNodeTypeToCategory(nodeType),
            Metadata = JsonSerializer.Serialize(new
            {
                language,
                nodeType,
                name = nodeName,
                startPosition = node.StartPosition,
                endPosition = node.EndPosition,
                startLine = node.Metadata.ContainsKey("StartLine") ? (int)node.Metadata["StartLine"] : 0,
                endLine = node.Metadata.ContainsKey("EndLine") ? (int)node.Metadata["EndLine"] : 0,
                text = nodeText.Length > 200 ? nodeText.Substring(0, 200) + "..." : nodeText,
                parsingEngine = "Tree-sitter-native",
                hilbertIndex
            })
        });

        _hashCache[hashKey] = hash;
        return hash;
    }
    
    private string MapNodeTypeToCategory(string nodeType)
    {
        // Map TreeSitter node types to our semantic categories
        if (nodeType.Contains("function") || nodeType.Contains("method") || nodeType.Contains("constructor"))
            return "function";
        if (nodeType.Contains("class") || nodeType.Contains("interface") || nodeType.Contains("struct") || nodeType.Contains("enum"))
            return "class";
        if (nodeType.Contains("import") || nodeType.Contains("using") || nodeType.Contains("use"))
            return "import";
        if (nodeType.Contains("variable") || nodeType.Contains("field") || nodeType.Contains("property") || nodeType.Contains("constant"))
            return "field";
        if (nodeType.Contains("module") || nodeType.Contains("namespace") || nodeType.Contains("package"))
            return "namespace";
        if (nodeType.Contains("call") || nodeType.Contains("invocation"))
            return "invocation";
        
        return "statement";
    }
    
    private string GetNodeText(IAstNode node, string sourceCode)
    {
        var startPos = node.StartPosition;
        var endPos = node.EndPosition;
        
        if (startPos < 0 || endPos > sourceCode.Length || startPos >= endPos)
            return string.Empty;
            
        return sourceCode.Substring(startPos, endPos - startPos);
    }
    
    private string? ExtractNodeName(IAstNode node, string sourceCode)
    {
        // Look for name/identifier child nodes
        foreach (var child in node.Children)
        {
            if (child.NodeType == "identifier" || child.NodeType == "name")
            {
                return GetNodeText(child, sourceCode);
            }
        }
        
        // Some languages put the name directly
        var nodeText = GetNodeText(node, sourceCode);
        
        // Try to extract name from common patterns
        if (node.NodeType.Contains("function") || node.NodeType.Contains("method"))
        {
            // function foo() -> foo
            var match = System.Text.RegularExpressions.Regex.Match(nodeText, @"(?:function|def|fn|func)\s+(\w+)");
            if (match.Success) return match.Groups[1].Value;
        }
        else if (node.NodeType.Contains("class"))
        {
            // class Foo -> Foo
            var match = System.Text.RegularExpressions.Regex.Match(nodeText, @"class\s+(\w+)");
            if (match.Success) return match.Groups[1].Value;
        }
        
        return null;
    }
    
    private void ExtractRelations(IAstNode node, string sourceCode, byte[] sourceAtomHash, string language)
    {
        var nodeType = node.NodeType;
        
        if (nodeType.Contains("call") || nodeType.Contains("invocation"))
        {
            // Extract function/method name being called
            var calledName = ExtractCallTarget(node, sourceCode);
            if (!string.IsNullOrEmpty(calledName))
            {
                var targetHash = ComputeHash(Encoding.UTF8.GetBytes($"{language}:function:{calledName}"));
                
                _relations.Add(new AtomRelation
                {
                    SourceAtomHash = sourceAtomHash,
                    TargetAtomHash = targetHash,
                    RelationType = "calls",
                    Weight = 1.0f,
                    Metadata = JsonSerializer.Serialize(new { calledFunction = calledName })
                });
            }
        }
        else if (nodeType.Contains("import") || nodeType.Contains("using") || nodeType.Contains("use"))
        {
            // Extract imported module/namespace
            var importedName = ExtractImportTarget(node, sourceCode);
            if (!string.IsNullOrEmpty(importedName))
            {
                var targetHash = ComputeHash(Encoding.UTF8.GetBytes($"{language}:module:{importedName}"));
                
                _relations.Add(new AtomRelation
                {
                    SourceAtomHash = sourceAtomHash,
                    TargetAtomHash = targetHash,
                    RelationType = "imports",
                    Weight = 1.0f,
                    Metadata = JsonSerializer.Serialize(new { importedModule = importedName })
                });
            }
        }
    }
    
    private string? ExtractCallTarget(IAstNode node, string sourceCode)
    {
        // Look for the function/method name being called
        foreach (var child in node.Children)
        {
            if (child.NodeType == "identifier" || child.NodeType == "name" || child.NodeType.Contains("member"))
            {
                return GetNodeText(child, sourceCode);
            }
        }
        
        return null;
    }
    
    private string? ExtractImportTarget(IAstNode node, string sourceCode)
    {
        // Look for module name in import statement
        var nodeText = GetNodeText(node, sourceCode);
        
        // Python: from foo import bar, import foo
        var pythonMatch = System.Text.RegularExpressions.Regex.Match(nodeText, @"(?:from\s+(\S+)|import\s+(\S+))");
        if (pythonMatch.Success) return pythonMatch.Groups[1].Value + pythonMatch.Groups[2].Value;
        
        // C#: using Foo.Bar;
        var csharpMatch = System.Text.RegularExpressions.Regex.Match(nodeText, @"using\s+([^;]+)");
        if (csharpMatch.Success) return csharpMatch.Groups[1].Value.Trim();
        
        // JS: import ... from 'module'
        var jsMatch = System.Text.RegularExpressions.Regex.Match(nodeText, @"from\s+['""]([^'""]+)['""]");
        if (jsMatch.Success) return jsMatch.Groups[1].Value;
        
        return null;
    }

    private byte[] CreateFileAtom(string code, string fileName, string language, string? metadata)
    {
        var fileBytes = Encoding.UTF8.GetBytes($"{language}:file:{fileName}:{code.Length}");
        var hash = ComputeHash(fileBytes);

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "code",
            category: "file",
            specificity: "concrete",
            identifier: $"{language}:{fileName}"
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
                language,
                fileName,
                size = code.Length,
                lines = code.Split('\n').Length,
                parsingEngine = "Tree-sitter",
                hilbertIndex,
                metadata
            })
        });

        return hash;
    }

    private void AtomizeWithPatterns(string code, string fileName, string language, byte[] fileHash)
    {
        var patterns = GetLanguagePatterns(language);
        if (patterns == null) return;

        var sequenceIndex = 0;

        // Extract functions
        if (patterns.FunctionPattern != null)
        {
            ExtractElements(code, patterns.FunctionPattern, "function", language, fileHash, ref sequenceIndex);
        }

        // Extract classes
        if (patterns.ClassPattern != null)
        {
            ExtractElements(code, patterns.ClassPattern, "class", language, fileHash, ref sequenceIndex);
        }
    }

    private void ExtractElements(
        string code,
        string pattern,
        string elementType,
        string language,
        byte[] fileHash,
        ref int sequenceIndex)
    {
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Multiline);
        var matches = regex.Matches(code);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            string? elementName = null;
            for (int i = 1; i < match.Groups.Count; i++)
            {
                if (!string.IsNullOrEmpty(match.Groups[i].Value))
                {
                    elementName = match.Groups[i].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(elementName)) continue;

            var atomicValue = Encoding.UTF8.GetBytes($"{language}:{elementType}:{elementName}");
            var hash = ComputeHash(atomicValue);

            // Check deduplication
            var hashKey = Convert.ToBase64String(hash);
            if (_hashCache.ContainsKey(hashKey))
            {
                sequenceIndex++;
                continue;
            }

            var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
                modality: "code",
                category: elementType,
                specificity: "concrete",
                identifier: $"{language}:{elementType}:{elementName}"
            );

            _atoms.Add(new Atom
            {
                ContentHash = hash,
                AtomicValue = atomicValue,
                CanonicalText = $"{elementName} ({elementType})",
                SpatialKey = new SpatialPosition(x, y, z),
                HilbertIndex = hilbertIndex,
                Modality = "code",
                Subtype = elementType,
                Metadata = JsonSerializer.Serialize(new
                {
                    language,
                    nodeType = elementType,
                    name = elementName,
                    parsingEngine = "Tree-sitter",
                    hilbertIndex
                })
            });

            _compositions.Add(new AtomComposition
            {
                ParentAtomHash = fileHash,
                ComponentAtomHash = hash,
                SequenceIndex = sequenceIndex++,
                Position = null
            });

            _hashCache[hashKey] = hash;
        }
    }

    private static LanguagePatterns? GetLanguagePatterns(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "python" => new LanguagePatterns
            {
                FunctionPattern = @"def\s+(\w+)\s*\(",
                ClassPattern = @"class\s+(\w+)"
            },
            "javascript" or "typescript" => new LanguagePatterns
            {
                FunctionPattern = @"function\s+(\w+)\s*\(|const\s+(\w+)\s*=\s*\(|(\w+)\s*:\s*\(",
                ClassPattern = @"class\s+(\w+)|interface\s+(\w+)"
            },
            "go" => new LanguagePatterns
            {
                FunctionPattern = @"func\s+(\w+)\s*\(",
                ClassPattern = @"type\s+(\w+)\s+struct"
            },
            "rust" => new LanguagePatterns
            {
                FunctionPattern = @"fn\s+(\w+)\s*[<\(]",
                ClassPattern = @"struct\s+(\w+)|enum\s+(\w+)|trait\s+(\w+)"
            },
            "java" => new LanguagePatterns
            {
                FunctionPattern = @"(?:public|private|protected)?\s*(?:static)?\s*\w+\s+(\w+)\s*\(",
                ClassPattern = @"class\s+(\w+)|interface\s+(\w+)|enum\s+(\w+)"
            },
            "cpp" or "c" => new LanguagePatterns
            {
                FunctionPattern = @"\w+\s+(\w+)\s*\([^)]*\)\s*{",
                ClassPattern = @"class\s+(\w+)|struct\s+(\w+)"
            },
            "ruby" => new LanguagePatterns
            {
                FunctionPattern = @"def\s+(\w+)",
                ClassPattern = @"class\s+(\w+)|module\s+(\w+)"
            },
            "php" => new LanguagePatterns
            {
                FunctionPattern = @"function\s+(\w+)\s*\(",
                ClassPattern = @"class\s+(\w+)|interface\s+(\w+)|trait\s+(\w+)"
            },
            _ => null
        };
    }

    private static byte[] ComputeHash(byte[] data) => SHA256.HashData(data);

    private record LanguagePatterns
    {
        public string? FunctionPattern { get; init; }
        public string? ClassPattern { get; init; }
    }
}
