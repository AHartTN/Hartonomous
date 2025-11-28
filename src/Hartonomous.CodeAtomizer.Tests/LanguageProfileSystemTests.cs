using System.Text;
using Hartonomous.CodeAtomizer.Core.Atomizers;
using Hartonomous.CodeAtomizer.Core.Models;
using Hartonomous.CodeAtomizer.Core.Services;

namespace Hartonomous.CodeAtomizer.Tests;

/// <summary>
/// Demonstrates the Universal Grammar Engine:
/// 1. Load language profiles (Python, C#) from JSON
/// 2. Atomize the profiles themselves into the knowledge graph
/// 3. Query relationships between languages
/// 4. Show how configuration IS atoms
/// </summary>
public class LanguageProfileSystemTests
{
    private static string GetProfilePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "language-profiles");
    }

    [Fact]
    public async Task Should_Load_Language_Profiles_From_Json()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        
        // Act
        var loadedCount = await loader.LoadProfilesAsync();
        
        // Assert
        Assert.True(loadedCount >= 2, $"Expected at least 2 profiles, loaded {loadedCount}");
        
        var pythonProfile = loader.GetProfile("python");
        Assert.NotNull(pythonProfile);
        Assert.Equal("Python", pythonProfile.DisplayName);
        Assert.Contains("py", pythonProfile.FileExtensions);
        Assert.Contains("object-oriented", pythonProfile.Paradigms);
        
        var csharpProfile = loader.GetProfile("csharp");
        Assert.NotNull(csharpProfile);
        Assert.Equal("C#", csharpProfile.DisplayName);
        Assert.Contains("cs", csharpProfile.FileExtensions);
    }

    [Fact]
    public async Task Should_Atomize_Language_Profile_Into_Knowledge_Graph()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        var pythonProfile = loader.GetProfile("python");
        Assert.NotNull(pythonProfile);
        
        var atomizer = new LanguageProfileAtomizer();
        
        // Act - Atomize the profile itself!
        var result = atomizer.Atomize(pythonProfile);
        
        // Assert
        Assert.NotNull(result.ProfileAtom);
        Assert.Equal("config", result.ProfileAtom.Modality);
        Assert.Equal("language_profile", result.ProfileAtom.Subtype);
        Assert.Contains("Python", result.ProfileAtom.CanonicalText);
        
        // Profile contains semantic mappings
        Assert.NotEmpty(result.MappingAtoms);
        Assert.Contains(result.MappingAtoms, a => 
            a.CanonicalText.Contains("function_definition") && 
            a.CanonicalText.Contains("function"));
        
        // Profile contains relation rules
        Assert.NotEmpty(result.RelationAtoms);
        Assert.Contains(result.RelationAtoms, a => 
            a.CanonicalText.Contains("calls") || 
            a.CanonicalText.Contains("imports"));
        
        // Profile contains metadata extractors
        Assert.NotEmpty(result.ExtractorAtoms);
        Assert.Contains(result.ExtractorAtoms, a => 
            a.CanonicalText.Contains("decorators") || 
            a.CanonicalText.Contains("docstring"));
        
        // Compositions: profile CONTAINS all components
        Assert.NotEmpty(result.Compositions);
        var profileHash = result.ProfileAtom.ContentHash;
        Assert.All(result.Compositions, comp => 
            Assert.Equal(profileHash, comp.ParentAtomHash));
    }

    [Fact]
    public async Task Should_Find_Similar_Languages_By_Paradigm()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        
        // Act - Find languages similar to Python
        var similarToPython = loader.FindSimilarLanguages("python", topN: 3);
        
        // Assert
        Assert.NotEmpty(similarToPython);
        // C# shares object-oriented paradigm with Python
        Assert.Contains("csharp", similarToPython, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_Group_Languages_By_Paradigm()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        
        // Act
        var languagesByParadigm = loader.GetLanguagesByParadigm();
        
        // Assert
        Assert.NotEmpty(languagesByParadigm);
        Assert.True(languagesByParadigm.ContainsKey("object-oriented"));
        
        var ooLanguages = languagesByParadigm["object-oriented"];
        Assert.Contains("python", ooLanguages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("csharp", ooLanguages, StringComparer.OrdinalIgnoreCase);
        
        // Functional paradigm
        Assert.True(languagesByParadigm.ContainsKey("functional"));
    }

    [Fact]
    public async Task Should_Query_Profile_By_File_Extension()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        
        // Act
        var pythonProfile = loader.GetProfileByExtension(".py");
        var csharpProfile = loader.GetProfileByExtension("cs"); // Also works without dot
        
        // Assert
        Assert.NotNull(pythonProfile);
        Assert.Equal("python", pythonProfile.Language);
        
        Assert.NotNull(csharpProfile);
        Assert.Equal("csharp", csharpProfile.Language);
    }

    [Fact]
    public async Task Should_Extract_Semantic_Mappings_From_Profile()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        var pythonProfile = loader.GetProfile("python");
        Assert.NotNull(pythonProfile);
        
        // Act - Query semantic mappings
        var functionMapping = pythonProfile.SemanticMappings["function_definition"];
        var classMapping = pythonProfile.SemanticMappings["class_definition"];
        
        // Assert
        Assert.Equal("function", functionMapping.Category);
        Assert.Equal("concrete", functionMapping.Specificity);
        Assert.True(functionMapping.Atomize);
        Assert.Equal("name", functionMapping.NamePath);
        
        Assert.Equal("class", classMapping.Category);
        Assert.Contains("type", classMapping.Tags);
    }

    [Fact]
    public async Task Should_Extract_Relation_Rules_From_Profile()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        var pythonProfile = loader.GetProfile("python");
        Assert.NotNull(pythonProfile);
        
        // Act - Query relation rules
        var callRule = pythonProfile.RelationRules.FirstOrDefault(r => r.RelationType == "calls");
        var importRule = pythonProfile.RelationRules.FirstOrDefault(r => r.RelationType == "imports");
        var inheritRule = pythonProfile.RelationRules.FirstOrDefault(r => r.RelationType == "inherits");
        
        // Assert
        Assert.NotNull(callRule);
        Assert.Equal("call", callRule.SourcePattern);
        Assert.Equal("function", callRule.TargetPath);
        
        Assert.NotNull(importRule);
        Assert.Contains("import", importRule.SourcePattern);
        
        Assert.NotNull(inheritRule);
        Assert.Equal("class_definition", inheritRule.SourcePattern);
        Assert.Equal("bases", inheritRule.TargetPath);
    }

    [Fact]
    public async Task Should_Extract_Metadata_Extractors_From_Profile()
    {
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        var pythonProfile = loader.GetProfile("python");
        Assert.NotNull(pythonProfile);
        Assert.NotNull(pythonProfile.MetadataExtractors);
        
        // Act
        var decoratorExtractor = pythonProfile.MetadataExtractors["decorators"];
        var docstringExtractor = pythonProfile.MetadataExtractors["docstring"];
        var typeHintsExtractor = pythonProfile.MetadataExtractors["type_hints"];
        
        // Assert
        Assert.Equal("child_nodes", decoratorExtractor.Strategy);
        Assert.Equal("decorator_list", decoratorExtractor.Path);
        Assert.True(decoratorExtractor.Multiple);
        Assert.Contains("function_definition", decoratorExtractor.AppliesTo);
        
        Assert.Equal("first_string_literal", docstringExtractor.Strategy);
        Assert.False(docstringExtractor.Multiple);
        
        Assert.Equal("child_nodes", typeHintsExtractor.Strategy);
    }

    [Fact]
    public async Task Demonstrate_Universal_Grammar_Engine_Concept()
    {
        // This test demonstrates the "Rosetta Stone" concept:
        // The same system can handle Python, C#, and (theoretically) Hieroglyphs
        
        // Arrange
        var loader = new LanguageProfileLoader(GetProfilePath());
        await loader.LoadProfilesAsync();
        var profileAtomizer = new LanguageProfileAtomizer();
        
        var output = new StringBuilder();
        output.AppendLine("=== UNIVERSAL GRAMMAR ENGINE DEMONSTRATION ===\n");
        
        // Act - Atomize all loaded profiles
        foreach (var profile in loader.GetAllProfiles())
        {
            var result = profileAtomizer.Atomize(profile);
            
            output.AppendLine($"Language: {profile.DisplayName} v{profile.Version}");
            output.AppendLine($"  Paradigms: {string.Join(", ", profile.Paradigms ?? Array.Empty<string>())}");
            output.AppendLine($"  File Extensions: {string.Join(", ", profile.FileExtensions)}");
            output.AppendLine($"  Semantic Mappings: {result.MappingAtoms.Length}");
            output.AppendLine($"  Relation Rules: {result.RelationAtoms.Length}");
            output.AppendLine($"  Metadata Extractors: {result.ExtractorAtoms.Length}");
            output.AppendLine($"  Total Atoms: {result.MappingAtoms.Length + result.RelationAtoms.Length + result.ExtractorAtoms.Length + 1}");
            output.AppendLine($"  Spatial Position: ({result.ProfileAtom.SpatialKey.X:F2}, {result.ProfileAtom.SpatialKey.Y:F2}, {result.ProfileAtom.SpatialKey.Z:F2})");
            output.AppendLine($"  Hilbert Index: {result.ProfileAtom.HilbertIndex}\n");
        }
        
        output.AppendLine("=== KEY INSIGHT ===");
        output.AppendLine("Each language profile is itself atomized into the knowledge graph.");
        output.AppendLine("To add support for Hieroglyphs, Linear B, or Musical Notation:");
        output.AppendLine("  1. Create a JSON profile (hieroglyphs.json)");
        output.AppendLine("  2. Define semantic mappings (cartouche=type, decree=function)");
        output.AppendLine("  3. NO CODE CHANGES REQUIRED");
        output.AppendLine("\nThe configuration IS atoms. Code IS atoms. Knowledge IS atoms.");
        output.AppendLine("This is a Universal Grammar Engine.");
        
        // Assert
        Assert.True(loader.GetAllProfiles().Count >= 2, "Need at least 2 profiles for demonstration");
        
        // Output for human verification
        Console.WriteLine(output.ToString());
    }
}
