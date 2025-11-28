using System.Text.Json.Serialization;

namespace Hartonomous.CodeAtomizer.Core.Models;

/// <summary>
/// Language-specific semantic profile that configures how the universal TreeSitter atomizer
/// interprets AST nodes for a particular language. These profiles are themselves atomized
/// and stored in the database for versioning, querying, and evolution.
/// </summary>
public sealed record LanguageSemanticProfile
{
    /// <summary>
    /// Language identifier (must match TreeSitter grammar name)
    /// </summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }
    
    /// <summary>
    /// Human-readable display name
    /// </summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }
    
    /// <summary>
    /// Profile version (for evolution tracking)
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }
    
    /// <summary>
    /// File extensions this profile handles
    /// </summary>
    [JsonPropertyName("file_extensions")]
    public required string[] FileExtensions { get; init; }
    
    /// <summary>
    /// TreeSitter grammar name (may differ from language identifier)
    /// </summary>
    [JsonPropertyName("tree_sitter_grammar")]
    public required string TreeSitterGrammar { get; init; }
    
    /// <summary>
    /// Semantic node type mappings: TreeSitter node type -> semantic category
    /// </summary>
    [JsonPropertyName("semantic_mappings")]
    public required Dictionary<string, SemanticNodeMapping> SemanticMappings { get; init; }
    
    /// <summary>
    /// Relation extraction rules (how to detect calls, imports, inheritance, etc.)
    /// </summary>
    [JsonPropertyName("relation_rules")]
    public required RelationRule[] RelationRules { get; init; }
    
    /// <summary>
    /// Metadata extraction patterns (decorators, type hints, docstrings, etc.)
    /// </summary>
    [JsonPropertyName("metadata_extractors")]
    public Dictionary<string, MetadataExtractor>? MetadataExtractors { get; init; }
    
    /// <summary>
    /// Language paradigm tags for clustering in semantic space
    /// </summary>
    [JsonPropertyName("paradigms")]
    public string[]? Paradigms { get; init; }
    
    /// <summary>
    /// Additional language-specific metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Maps a TreeSitter node type to a semantic category and provides context
/// </summary>
public sealed record SemanticNodeMapping
{
    /// <summary>
    /// Semantic category (function, class, import, variable, etc.)
    /// </summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }
    
    /// <summary>
    /// Specificity level (abstract, generic, concrete)
    /// </summary>
    [JsonPropertyName("specificity")]
    public string? Specificity { get; init; }
    
    /// <summary>
    /// Whether this node should create an atom
    /// </summary>
    [JsonPropertyName("atomize")]
    public bool Atomize { get; init; } = true;
    
    /// <summary>
    /// Child node path to extract the name (e.g., "name.identifier", "declarator.name")
    /// </summary>
    [JsonPropertyName("name_path")]
    public string? NamePath { get; init; }
    
    /// <summary>
    /// Additional tags for semantic clustering
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }
    
    /// <summary>
    /// Weight for importance scoring (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; init; } = 1.0;
}

/// <summary>
/// Defines how to extract relations between atoms (calls, imports, inheritance, etc.)
/// </summary>
public sealed record RelationRule
{
    /// <summary>
    /// Rule identifier
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    /// <summary>
    /// Relation type (calls, imports, inherits, defines, etc.)
    /// </summary>
    [JsonPropertyName("relation_type")]
    public required string RelationType { get; init; }
    
    /// <summary>
    /// Source node type pattern (regex supported)
    /// </summary>
    [JsonPropertyName("source_pattern")]
    public required string SourcePattern { get; init; }
    
    /// <summary>
    /// Target node type pattern (regex supported)
    /// </summary>
    [JsonPropertyName("target_pattern")]
    public string? TargetPattern { get; init; }
    
    /// <summary>
    /// Child path to find the target (e.g., "function.name", "module")
    /// </summary>
    [JsonPropertyName("target_path")]
    public string? TargetPath { get; init; }
    
    /// <summary>
    /// Relation weight/strength (0.0 to 1.0)
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; init; } = 1.0;
    
    /// <summary>
    /// Bidirectional relation (create inverse relation)
    /// </summary>
    [JsonPropertyName("bidirectional")]
    public bool Bidirectional { get; init; } = false;
    
    /// <summary>
    /// Inverse relation type (for bidirectional relations)
    /// </summary>
    [JsonPropertyName("inverse_relation_type")]
    public string? InverseRelationType { get; init; }
}

/// <summary>
/// Defines how to extract language-specific metadata from AST nodes
/// </summary>
public sealed record MetadataExtractor
{
    /// <summary>
    /// Extractor identifier
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    /// <summary>
    /// Node types this extractor applies to
    /// </summary>
    [JsonPropertyName("applies_to")]
    public required string[] AppliesTo { get; init; }
    
    /// <summary>
    /// Extraction strategy (child_nodes, attribute, text_pattern, first_string_literal, etc.)
    /// </summary>
    [JsonPropertyName("strategy")]
    public required string Strategy { get; init; }
    
    /// <summary>
    /// Child node path or attribute name (strategy-dependent)
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }
    
    /// <summary>
    /// Regex pattern for text extraction
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }
    
    /// <summary>
    /// Output metadata key
    /// </summary>
    [JsonPropertyName("output_key")]
    public required string OutputKey { get; init; }
    
    /// <summary>
    /// Whether to extract multiple values (array)
    /// </summary>
    [JsonPropertyName("multiple")]
    public bool Multiple { get; init; } = false;
}

/// <summary>
/// Result of atomizing a language profile
/// </summary>
public sealed record LanguageProfileAtomizationResult
{
    /// <summary>
    /// The profile atom (root)
    /// </summary>
    public required Atom ProfileAtom { get; init; }
    
    /// <summary>
    /// Semantic mapping atoms
    /// </summary>
    public required Atom[] MappingAtoms { get; init; }
    
    /// <summary>
    /// Relation rule atoms
    /// </summary>
    public required Atom[] RelationAtoms { get; init; }
    
    /// <summary>
    /// Metadata extractor atoms
    /// </summary>
    public required Atom[] ExtractorAtoms { get; init; }
    
    /// <summary>
    /// All compositions (profile contains mappings/rules/extractors)
    /// </summary>
    public required AtomComposition[] Compositions { get; init; }
    
    /// <summary>
    /// Relations between components
    /// </summary>
    public required AtomRelation[] Relations { get; init; }
}
