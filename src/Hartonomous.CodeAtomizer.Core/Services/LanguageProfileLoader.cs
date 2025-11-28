using System.Text.Json;
using Hartonomous.CodeAtomizer.Core.Models;

namespace Hartonomous.CodeAtomizer.Core.Services;

/// <summary>
/// Loads and manages language semantic profiles from JSON configuration files.
/// Profiles define how the universal TreeSitter atomizer interprets different languages.
/// </summary>
public sealed class LanguageProfileLoader
{
    private readonly Dictionary<string, LanguageSemanticProfile> _profiles = new();
    private readonly string _profileDirectory;

    public LanguageProfileLoader(string? profileDirectory = null)
    {
        _profileDirectory = profileDirectory ?? 
            Path.Combine(AppContext.BaseDirectory, "config", "language-profiles");
    }

    /// <summary>
    /// Load all language profiles from the configuration directory
    /// </summary>
    public async Task<int> LoadProfilesAsync()
    {
        if (!Directory.Exists(_profileDirectory))
        {
            Console.WriteLine($"Profile directory not found: {_profileDirectory}");
            return 0;
        }

        var jsonFiles = Directory.GetFiles(_profileDirectory, "*.json", SearchOption.TopDirectoryOnly);
        var loadedCount = 0;

        foreach (var filePath in jsonFiles)
        {
            try
            {
                var profile = await LoadProfileFromFileAsync(filePath);
                if (profile != null)
                {
                    _profiles[profile.Language.ToLowerInvariant()] = profile;
                    loadedCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load profile from {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Loaded {loadedCount} language profile(s) from {_profileDirectory}");
        return loadedCount;
    }

    /// <summary>
    /// Load a single language profile from a JSON file
    /// </summary>
    public async Task<LanguageSemanticProfile?> LoadProfileFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Profile file not found: {filePath}");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var profile = JsonSerializer.Deserialize<LanguageSemanticProfile>(json, options);
            
            if (profile == null)
            {
                Console.WriteLine($"Failed to deserialize profile from {Path.GetFileName(filePath)}");
                return null;
            }

            Console.WriteLine($"Loaded {profile.DisplayName} profile v{profile.Version}");
            return profile;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON parsing error in {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading profile from {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get a loaded profile by language identifier
    /// </summary>
    public LanguageSemanticProfile? GetProfile(string language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        return _profiles.TryGetValue(language.ToLowerInvariant(), out var profile) 
            ? profile 
            : null;
    }

    /// <summary>
    /// Get a profile by file extension
    /// </summary>
    public LanguageSemanticProfile? GetProfileByExtension(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
            return null;

        var ext = fileExtension.TrimStart('.').ToLowerInvariant();
        
        foreach (var profile in _profiles.Values)
        {
            if (profile.FileExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                return profile;
        }

        return null;
    }

    /// <summary>
    /// Get all loaded profiles
    /// </summary>
    public IReadOnlyCollection<LanguageSemanticProfile> GetAllProfiles()
    {
        return _profiles.Values.ToArray();
    }

    /// <summary>
    /// Check if a profile is loaded for a given language
    /// </summary>
    public bool HasProfile(string language)
    {
        return !string.IsNullOrEmpty(language) && 
               _profiles.ContainsKey(language.ToLowerInvariant());
    }

    /// <summary>
    /// Get supported file extensions across all loaded profiles
    /// </summary>
    public string[] GetSupportedExtensions()
    {
        return _profiles.Values
            .SelectMany(p => p.FileExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Get languages grouped by paradigm
    /// </summary>
    public Dictionary<string, List<string>> GetLanguagesByParadigm()
    {
        var paradigmMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in _profiles.Values)
        {
            if (profile.Paradigms == null) continue;

            foreach (var paradigm in profile.Paradigms)
            {
                if (!paradigmMap.ContainsKey(paradigm))
                    paradigmMap[paradigm] = new List<string>();

                paradigmMap[paradigm].Add(profile.Language);
            }
        }

        return paradigmMap;
    }

    /// <summary>
    /// Find languages with similar semantic features
    /// </summary>
    public List<string> FindSimilarLanguages(string language, int topN = 5)
    {
        var sourceProfile = GetProfile(language);
        if (sourceProfile == null) return new List<string>();

        var scores = new Dictionary<string, double>();

        foreach (var profile in _profiles.Values)
        {
            if (profile.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                continue;

            var score = ComputeSimilarityScore(sourceProfile, profile);
            scores[profile.Language] = score;
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => kv.Key)
            .ToList();
    }

    private static double ComputeSimilarityScore(LanguageSemanticProfile a, LanguageSemanticProfile b)
    {
        var score = 0.0;

        // Paradigm overlap
        if (a.Paradigms != null && b.Paradigms != null)
        {
            var commonParadigms = a.Paradigms.Intersect(b.Paradigms, StringComparer.OrdinalIgnoreCase).Count();
            var totalParadigms = a.Paradigms.Union(b.Paradigms, StringComparer.OrdinalIgnoreCase).Count();
            score += (double)commonParadigms / totalParadigms * 0.4;
        }

        // Semantic category overlap
        var aCategories = a.SemanticMappings.Values.Select(m => m.Category).Distinct().ToHashSet();
        var bCategories = b.SemanticMappings.Values.Select(m => m.Category).Distinct().ToHashSet();
        var commonCategories = aCategories.Intersect(bCategories).Count();
        var totalCategories = aCategories.Union(bCategories).Count();
        score += (double)commonCategories / totalCategories * 0.3;

        // Relation type overlap
        var aRelations = a.RelationRules.Select(r => r.RelationType).Distinct().ToHashSet();
        var bRelations = b.RelationRules.Select(r => r.RelationType).Distinct().ToHashSet();
        var commonRelations = aRelations.Intersect(bRelations).Count();
        var totalRelations = aRelations.Union(bRelations).Count();
        score += (double)commonRelations / totalRelations * 0.3;

        return score;
    }
}
