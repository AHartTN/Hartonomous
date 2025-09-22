using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Discovers and manages Ollama models for ingestion
/// Scans Ollama directory and provides model metadata
/// </summary>
public class OllamaDiscoveryService
{
    private readonly ILogger<OllamaDiscoveryService> _logger;
    private readonly string _ollamaModelsPath;

    public OllamaDiscoveryService(IConfiguration configuration, ILogger<OllamaDiscoveryService> logger)
    {
        _logger = logger;

        // Default Ollama model paths by OS
        _ollamaModelsPath = configuration["Ollama:ModelsPath"] ?? GetDefaultOllamaPath();
    }

    /// <summary>
    /// Discover all available Ollama models
    /// </summary>
    public async Task<List<DiscoveredModel>> DiscoverModelsAsync()
    {
        var models = new List<DiscoveredModel>();

        try
        {
            if (!Directory.Exists(_ollamaModelsPath))
            {
                _logger.LogWarning("Ollama models directory not found: {Path}", _ollamaModelsPath);
                return models;
            }

            _logger.LogInformation("Scanning Ollama models directory: {Path}", _ollamaModelsPath);

            // Scan manifests directory for model metadata
            var manifestsPath = Path.Combine(_ollamaModelsPath, "manifests");
            if (Directory.Exists(manifestsPath))
            {
                await ScanManifestsAsync(manifestsPath, models);
            }

            // Scan blobs directory for actual model files
            var blobsPath = Path.Combine(_ollamaModelsPath, "blobs");
            if (Directory.Exists(blobsPath))
            {
                await EnrichWithBlobDataAsync(blobsPath, models);
            }

            _logger.LogInformation("Discovered {Count} Ollama models", models.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering Ollama models");
        }

        return models.OrderBy(m => m.Name).ToList();
    }

    /// <summary>
    /// Get detailed information about a specific model
    /// </summary>
    public async Task<ModelDetails?> GetModelDetailsAsync(string modelName)
    {
        var models = await DiscoverModelsAsync();
        var model = models.FirstOrDefault(m => m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));

        if (model == null) return null;

        return new ModelDetails
        {
            Name = model.Name,
            FilePath = model.FilePath,
            SizeBytes = model.SizeBytes,
            Architecture = model.Architecture,
            ParameterCount = model.ParameterCount,
            Quantization = model.Quantization,
            CreatedAt = model.CreatedAt,
            IsValid = await ValidateModelFileAsync(model.FilePath)
        };
    }

    /// <summary>
    /// Validate that a model file can be ingested
    /// </summary>
    public async Task<ValidationResult> ValidateModelAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Model file does not exist"
                };
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Model file is empty"
                };
            }

            // Check if it's a valid GGUF file
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 4)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File too small to be a valid model"
                };
            }

            var magic = reader.ReadBytes(4);
            var magicString = System.Text.Encoding.ASCII.GetString(magic);

            if (magicString != "GGUF")
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Invalid file format. Expected GGUF, got: {magicString}"
                };
            }

            return new ValidationResult
            {
                IsValid = true,
                Message = "Model file is valid for ingestion"
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Validation error: {ex.Message}"
            };
        }
    }

    private async Task ScanManifestsAsync(string manifestsPath, List<DiscoveredModel> models)
    {
        try
        {
            var registryDirs = Directory.GetDirectories(manifestsPath);

            foreach (var registryDir in registryDirs)
            {
                var libraryDirs = Directory.GetDirectories(registryDir);

                foreach (var libraryDir in libraryDirs)
                {
                    var modelDirs = Directory.GetDirectories(libraryDir);

                    foreach (var modelDir in modelDirs)
                    {
                        await ScanModelManifestsAsync(modelDir, models);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning manifests directory: {Path}", manifestsPath);
        }
    }

    private async Task ScanModelManifestsAsync(string modelDir, List<DiscoveredModel> models)
    {
        try
        {
            var manifestFiles = Directory.GetFiles(modelDir, "*", SearchOption.AllDirectories);

            foreach (var manifestFile in manifestFiles)
            {
                try
                {
                    var manifestContent = await File.ReadAllTextAsync(manifestFile);
                    var manifest = JsonSerializer.Deserialize<JsonElement>(manifestContent);

                    var model = ParseManifest(manifestFile, manifest);
                    if (model != null)
                    {
                        models.Add(model);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not parse manifest: {File}", manifestFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning model manifests in: {Dir}", modelDir);
        }
    }

    private DiscoveredModel? ParseManifest(string manifestPath, JsonElement manifest)
    {
        try
        {
            // Extract model name from path
            var pathParts = manifestPath.Replace(_ollamaModelsPath, "").Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length < 4) return null;

            var modelName = $"{pathParts[2]}/{pathParts[3]}";

            // Try to extract metadata from manifest
            var config = manifest.TryGetProperty("config", out var configElement) ? configElement : (JsonElement?)null;

            return new DiscoveredModel
            {
                Name = modelName,
                ManifestPath = manifestPath,
                Architecture = ExtractStringProperty(config, "architecture") ?? "unknown",
                ParameterCount = ExtractLongProperty(config, "parameter_count"),
                Quantization = ExtractStringProperty(config, "quantization") ?? "unknown",
                CreatedAt = File.GetCreationTime(manifestPath)
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing manifest: {Path}", manifestPath);
            return null;
        }
    }

    private async Task EnrichWithBlobDataAsync(string blobsPath, List<DiscoveredModel> models)
    {
        try
        {
            var blobFiles = Directory.GetFiles(blobsPath, "sha256-*", SearchOption.TopDirectoryOnly);

            foreach (var model in models)
            {
                // Try to find corresponding blob file
                var blobFile = blobFiles.FirstOrDefault(f => await IsGGUFFileAsync(f));
                if (blobFile != null)
                {
                    var fileInfo = new FileInfo(blobFile);
                    model.FilePath = blobFile;
                    model.SizeBytes = fileInfo.Length;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error enriching models with blob data");
        }
    }

    private async Task<bool> IsGGUFFileAsync(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            if (stream.Length < 4) return false;

            using var reader = new BinaryReader(stream);
            var magic = reader.ReadBytes(4);
            return System.Text.Encoding.ASCII.GetString(magic) == "GGUF";
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ValidateModelFileAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;

        var result = await ValidateModelAsync(filePath);
        return result.IsValid;
    }

    private string GetDefaultOllamaPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
        }
        else
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
        }
    }

    private string? ExtractStringProperty(JsonElement? element, string propertyName)
    {
        if (element?.TryGetProperty(propertyName, out var prop) == true && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private long ExtractLongProperty(JsonElement? element, string propertyName)
    {
        if (element?.TryGetProperty(propertyName, out var prop) == true && prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt64();
        }
        return 0;
    }
}

public class DiscoveredModel
{
    public string Name { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? ManifestPath { get; set; }
    public long SizeBytes { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public string Quantization { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ModelDetails
{
    public string Name { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public long SizeBytes { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public string Quantization { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsValid { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}