using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hartonomous.CodeAtomizer.Core.Models;
using Hartonomous.CodeAtomizer.Core.Spatial;

namespace Hartonomous.CodeAtomizer.Core.Atomizers;

/// <summary>
/// Atomizes language semantic profiles themselves into the atom table.
/// This allows profiles to be versioned, queried, and composed like any other knowledge.
/// Profiles become atoms that define how code becomes atoms - recursive knowledge representation.
/// </summary>
public sealed class LanguageProfileAtomizer
{
    private readonly List<Atom> _atoms = new();
    private readonly List<AtomComposition> _compositions = new();
    private readonly List<AtomRelation> _relations = new();
    private readonly Dictionary<string, byte[]> _hashCache = new();

    /// <summary>
    /// Atomize a language semantic profile into the knowledge graph
    /// </summary>
    public LanguageProfileAtomizationResult Atomize(LanguageSemanticProfile profile)
    {
        _atoms.Clear();
        _compositions.Clear();
        _relations.Clear();
        _hashCache.Clear();

        // 1. Create root profile atom
        var profileHash = CreateProfileAtom(profile);

        // 2. Create semantic mapping atoms
        var mappingAtoms = new List<Atom>();
        foreach (var (nodeType, mapping) in profile.SemanticMappings)
        {
            var mappingHash = CreateSemanticMappingAtom(profile.Language, nodeType, mapping);
            mappingAtoms.Add(_atoms.First(a => a.ContentHash.SequenceEqual(mappingHash)));
            
            // Compose: profile CONTAINS mapping
            CreateComposition(profileHash, mappingHash, _compositions.Count);
            
            // Relate: mapping DEFINES semantic category
            CreateRelation(mappingHash, CreateCategoryAtom(mapping.Category), "defines", 1.0);
        }

        // 3. Create relation rule atoms
        var relationAtoms = new List<Atom>();
        foreach (var rule in profile.RelationRules)
        {
            var ruleHash = CreateRelationRuleAtom(profile.Language, rule);
            relationAtoms.Add(_atoms.First(a => a.ContentHash.SequenceEqual(ruleHash)));
            
            // Compose: profile CONTAINS rule
            CreateComposition(profileHash, ruleHash, _compositions.Count);
            
            // Relate: rule EXTRACTS relation type
            CreateRelation(ruleHash, CreateRelationTypeAtom(rule.RelationType), "extracts", rule.Weight);
        }

        // 4. Create metadata extractor atoms
        var extractorAtoms = new List<Atom>();
        if (profile.MetadataExtractors != null)
        {
            foreach (var (extractorId, extractor) in profile.MetadataExtractors)
            {
                var extractorHash = CreateMetadataExtractorAtom(profile.Language, extractorId, extractor);
                extractorAtoms.Add(_atoms.First(a => a.ContentHash.SequenceEqual(extractorHash)));
                
                // Compose: profile CONTAINS extractor
                CreateComposition(profileHash, extractorHash, _compositions.Count);
            }
        }

        // 5. Create paradigm relations (for semantic clustering)
        if (profile.Paradigms != null)
        {
            foreach (var paradigm in profile.Paradigms)
            {
                var paradigmHash = CreateParadigmAtom(paradigm);
                CreateRelation(profileHash, paradigmHash, "implements_paradigm", 0.8);
            }
        }

        return new LanguageProfileAtomizationResult
        {
            ProfileAtom = _atoms.First(a => a.ContentHash.SequenceEqual(profileHash)),
            MappingAtoms = mappingAtoms.ToArray(),
            RelationAtoms = relationAtoms.ToArray(),
            ExtractorAtoms = extractorAtoms.ToArray(),
            Compositions = _compositions.ToArray(),
            Relations = _relations.ToArray()
        };
    }

    private byte[] CreateProfileAtom(LanguageSemanticProfile profile)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"language_profile:{profile.Language}:{profile.Version}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        // Position language profiles in semantic space based on paradigm
        var paradigmVector = ComputeParadigmVector(profile.Paradigms);
        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "language_profile",
            specificity: "concrete",
            identifier: profile.Language
        );

        var metadata = new Dictionary<string, object>
        {
            ["modality"] = "config",
            ["subtype"] = "language_profile",
            ["language"] = profile.Language,
            ["display_name"] = profile.DisplayName,
            ["version"] = profile.Version,
            ["tree_sitter_grammar"] = profile.TreeSitterGrammar,
            ["file_extensions"] = profile.FileExtensions,
            ["paradigms"] = profile.Paradigms ?? Array.Empty<string>(),
            ["mapping_count"] = profile.SemanticMappings.Count,
            ["rule_count"] = profile.RelationRules.Length,
            ["extractor_count"] = profile.MetadataExtractors?.Count ?? 0
        };

        if (profile.Metadata != null)
        {
            foreach (var (key, value) in profile.Metadata)
                metadata[$"custom_{key}"] = value;
        }

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"{profile.DisplayName} v{profile.Version}",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "language_profile",
            Metadata = JsonSerializer.Serialize(metadata)
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private byte[] CreateSemanticMappingAtom(string language, string nodeType, SemanticNodeMapping mapping)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"semantic_mapping:{language}:{nodeType}:{mapping.Category}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "semantic_mapping",
            specificity: mapping.Specificity ?? "generic",
            identifier: $"{language}:{nodeType}"
        );

        var metadata = new Dictionary<string, object>
        {
            ["modality"] = "config",
            ["subtype"] = "semantic_mapping",
            ["language"] = language,
            ["node_type"] = nodeType,
            ["category"] = mapping.Category,
            ["specificity"] = mapping.Specificity ?? "generic",
            ["atomize"] = mapping.Atomize,
            ["weight"] = mapping.Weight
        };

        if (mapping.NamePath != null)
            metadata["name_path"] = mapping.NamePath;
        
        if (mapping.Tags != null)
            metadata["tags"] = mapping.Tags;

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"{language}: {nodeType} → {mapping.Category}",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "semantic_mapping",
            Metadata = JsonSerializer.Serialize(metadata)
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private byte[] CreateRelationRuleAtom(string language, RelationRule rule)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"relation_rule:{language}:{rule.Id}:{rule.RelationType}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "relation_rule",
            specificity: "concrete",
            identifier: $"{language}:{rule.Id}"
        );

        var metadata = new Dictionary<string, object>
        {
            ["modality"] = "config",
            ["subtype"] = "relation_rule",
            ["language"] = language,
            ["rule_id"] = rule.Id,
            ["relation_type"] = rule.RelationType,
            ["source_pattern"] = rule.SourcePattern,
            ["weight"] = rule.Weight,
            ["bidirectional"] = rule.Bidirectional
        };

        if (rule.TargetPattern != null)
            metadata["target_pattern"] = rule.TargetPattern;
        
        if (rule.TargetPath != null)
            metadata["target_path"] = rule.TargetPath;
        
        if (rule.InverseRelationType != null)
            metadata["inverse_relation_type"] = rule.InverseRelationType;

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"{language}: {rule.SourcePattern} → [{rule.RelationType}]",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "relation_rule",
            Metadata = JsonSerializer.Serialize(metadata)
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private byte[] CreateMetadataExtractorAtom(string language, string extractorId, MetadataExtractor extractor)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"metadata_extractor:{language}:{extractorId}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "metadata_extractor",
            specificity: "concrete",
            identifier: $"{language}:{extractorId}"
        );

        var metadata = new Dictionary<string, object>
        {
            ["modality"] = "config",
            ["subtype"] = "metadata_extractor",
            ["language"] = language,
            ["extractor_id"] = extractorId,
            ["strategy"] = extractor.Strategy,
            ["output_key"] = extractor.OutputKey,
            ["applies_to"] = extractor.AppliesTo,
            ["multiple"] = extractor.Multiple
        };

        if (extractor.Path != null)
            metadata["path"] = extractor.Path;
        
        if (extractor.Pattern != null)
            metadata["pattern"] = extractor.Pattern;

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"{language}: Extract {extractor.OutputKey} via {extractor.Strategy}",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "metadata_extractor",
            Metadata = JsonSerializer.Serialize(metadata)
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private byte[] CreateCategoryAtom(string category)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"semantic_category:{category}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "semantic_category",
            specificity: "abstract",
            identifier: category
        );

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"Category: {category}",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "semantic_category",
            Metadata = JsonSerializer.Serialize(new { category })
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private byte[] CreateRelationTypeAtom(string relationType)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"relation_type:{relationType}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "relation_type",
            specificity: "abstract",
            identifier: relationType
        );

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"Relation: {relationType}",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "relation_type",
            Metadata = JsonSerializer.Serialize(new { relation_type = relationType })
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private byte[] CreateParadigmAtom(string paradigm)
    {
        var atomicValue = Encoding.UTF8.GetBytes($"paradigm:{paradigm}");
        var hash = ComputeHash(atomicValue);

        if (_hashCache.ContainsKey(Convert.ToBase64String(hash)))
            return hash;

        var (x, y, z, hilbertIndex) = LandmarkProjection.ComputePositionWithHilbert(
            modality: "config",
            category: "paradigm",
            specificity: "abstract",
            identifier: paradigm
        );

        _atoms.Add(new Atom
        {
            ContentHash = hash,
            AtomicValue = atomicValue,
            CanonicalText = $"Paradigm: {paradigm}",
            SpatialKey = new SpatialPosition(x, y, z),
            HilbertIndex = hilbertIndex,
            Modality = "config",
            Subtype = "paradigm",
            Metadata = JsonSerializer.Serialize(new { paradigm })
        });

        _hashCache[Convert.ToBase64String(hash)] = hash;
        return hash;
    }

    private void CreateComposition(byte[] parentHash, byte[] childHash, int sequenceIndex)
    {
        _compositions.Add(new AtomComposition
        {
            ParentAtomHash = parentHash,
            ComponentAtomHash = childHash,
            SequenceIndex = sequenceIndex,
            Position = null
        });
    }

    private void CreateRelation(
        byte[] sourceHash,
        byte[] targetHash,
        string relationType,
        double weight = 1.0)
    {
        _relations.Add(new AtomRelation
        {
            SourceAtomHash = sourceHash,
            TargetAtomHash = targetHash,
            RelationType = relationType,
            Weight = weight,
            SpatialDistance = null,
            Metadata = null
        });
    }

    private static byte[] ComputeHash(byte[] data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// Compute a paradigm vector for spatial positioning
    /// Languages with similar paradigms cluster together
    /// </summary>
    private static (double x, double y, double z) ComputeParadigmVector(string[]? paradigms)
    {
        if (paradigms == null || paradigms.Length == 0)
            return (0.5, 0.5, 0.5);

        // Simple paradigm coordinate mapping
        var paradigmWeights = new Dictionary<string, (double x, double y, double z)>
        {
            ["imperative"] = (0.2, 0.2, 0.5),
            ["object-oriented"] = (0.5, 0.5, 0.7),
            ["functional"] = (0.8, 0.3, 0.6),
            ["declarative"] = (0.3, 0.8, 0.4),
            ["procedural"] = (0.3, 0.3, 0.5),
            ["concurrent"] = (0.7, 0.7, 0.8),
            ["scripting"] = (0.4, 0.6, 0.3),
            ["compiled"] = (0.6, 0.4, 0.7),
            ["interpreted"] = (0.4, 0.5, 0.3)
        };

        var x = 0.0;
        var y = 0.0;
        var z = 0.0;
        var count = 0;

        foreach (var paradigm in paradigms)
        {
            if (paradigmWeights.TryGetValue(paradigm.ToLowerInvariant(), out var coords))
            {
                x += coords.x;
                y += coords.y;
                z += coords.z;
                count++;
            }
        }

        return count > 0 ? (x / count, y / count, z / count) : (0.5, 0.5, 0.5);
    }
}
