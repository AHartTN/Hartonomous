using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Observability.Interfaces;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Agent loader service providing dynamic agent loading and capability registration
/// </summary>
public class AgentLoaderService : IAgentLoader, IDisposable
{
    private readonly ILogger<AgentLoaderService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly AgentClientConfiguration _configuration;
    private readonly SecurityValidator _securityValidator;
    private readonly ConcurrentDictionary<string, LoadedAgent> _loadedAgents = new();
    private readonly ConcurrentDictionary<string, PluginLoader> _pluginLoaders = new();
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private bool _disposed;

    public AgentLoaderService(
        ILogger<AgentLoaderService> logger,
        IMetricsCollector metricsCollector,
        ICapabilityRegistry capabilityRegistry,
        IOptions<AgentClientConfiguration> configuration,
        SecurityValidator securityValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _securityValidator = securityValidator ?? throw new ArgumentNullException(nameof(securityValidator));
    }

    public event EventHandler<AgentLoadedEventArgs>? AgentLoaded;
    public event EventHandler<AgentUnloadedEventArgs>? AgentUnloaded;
    public event EventHandler<AgentLoadFailedEventArgs>? AgentLoadFailed;

    public async Task<AgentDefinition> LoadAgentAsync(string agentPath, bool validate = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentPath)) throw new ArgumentNullException(nameof(agentPath));
        if (!File.Exists(agentPath) && !Directory.Exists(agentPath))
            throw new FileNotFoundException($"Agent path not found: {agentPath}");

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            AgentDefinition? definition = null;
            AgentValidationResult? validationResult = null;

            try
            {
                // Validate first if requested
                if (validate)
                {
                    validationResult = await ValidateAgentAsync(agentPath, cancellationToken);
                    if (!validationResult.IsValid)
                    {
                        var errors = string.Join(", ", validationResult.Errors);
                        throw new InvalidOperationException($"Agent validation failed: {errors}");
                    }
                }

                // Load agent definition
                definition = await LoadAgentDefinitionAsync(agentPath, cancellationToken);

                // Check if already loaded
                if (_loadedAgents.ContainsKey(definition.Id))
                {
                    throw new InvalidOperationException($"Agent {definition.Id} is already loaded");
                }

                // Create plugin loader
                var loader = CreatePluginLoader(agentPath, definition);
                var assembly = loader.LoadDefaultAssembly();

                // Create load context
                var loadContext = new AgentLoadContext
                {
                    AgentId = definition.Id,
                    LoadPath = agentPath,
                    LoadContextName = loader.GetType().Name,
                    LoadedAssemblies = new[] { assembly.FullName ?? "Unknown" },
                    IsCollectible = loader.IsUnloadable,
                    LoadedAt = DateTimeOffset.UtcNow,
                    MemoryUsage = GC.GetTotalMemory(false)
                };

                // Store loaded agent
                var loadedAgent = new LoadedAgent
                {
                    Definition = definition,
                    Assembly = assembly,
                    Loader = loader,
                    LoadContext = loadContext
                };

                _loadedAgents.TryAdd(definition.Id, loadedAgent);
                _pluginLoaders.TryAdd(definition.Id, loader);

                _logger.LogInformation("Loaded agent {AgentId} version {Version} from {Path}",
                    definition.Id, definition.Version, agentPath);

                _metricsCollector.IncrementCounter("agent.loader.loaded", tags: new Dictionary<string, string>
                {
                    ["agent_id"] = definition.Id,
                    ["agent_type"] = definition.Type.ToString()
                });

                // Fire event
                OnAgentLoaded(definition, loadContext);

                return definition;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load agent from {Path}", agentPath);

                _metricsCollector.IncrementCounter("agent.loader.load_failed", tags: new Dictionary<string, string>
                {
                    ["agent_path"] = agentPath,
                    ["error"] = ex.GetType().Name
                });

                // Fire event
                OnAgentLoadFailed(agentPath, ex, validationResult);

                throw;
            }
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task UnloadAgentAsync(string agentId, bool force = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));

        if (!_loadedAgents.TryGetValue(agentId, out var loadedAgent))
            return; // Already unloaded

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Unregister capabilities first
            await UnregisterAgentCapabilitiesAsync(agentId, cancellationToken: cancellationToken);

            // Remove from collections
            _loadedAgents.TryRemove(agentId, out _);
            _pluginLoaders.TryRemove(agentId, out var loader);

            // Dispose loader
            try
            {
                loader?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose plugin loader for agent {AgentId}", agentId);
            }

            // Trigger GC if the loader was collectible
            if (loadedAgent.LoadContext.IsCollectible)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            _logger.LogInformation("Unloaded agent {AgentId}", agentId);

            _metricsCollector.IncrementCounter("agent.loader.unloaded", tags: new Dictionary<string, string>
            {
                ["agent_id"] = agentId,
                ["force"] = force.ToString()
            });

            // Fire event
            OnAgentUnloaded(agentId, force);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload agent {AgentId}", agentId);
            if (!force) throw;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task<AgentDefinition> ReloadAgentAsync(string agentId, string agentPath, bool hotSwap = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentNullException(nameof(agentId));
        if (string.IsNullOrEmpty(agentPath)) throw new ArgumentNullException(nameof(agentPath));

        // Unload existing version
        await UnloadAgentAsync(agentId, cancellationToken: cancellationToken);

        // Load new version
        return await LoadAgentAsync(agentPath, cancellationToken: cancellationToken);
    }

    public async Task<AgentValidationResult> ValidateAgentAsync(string agentPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentPath)) throw new ArgumentNullException(nameof(agentPath));

        var errors = new List<string>();
        var warnings = new List<string>();
        var securityIssues = new List<string>();
        var performanceConcerns = new List<string>();
        var compatibilityIssues = new List<string>();

        try
        {
            // Check if path exists
            if (!File.Exists(agentPath) && !Directory.Exists(agentPath))
            {
                errors.Add($"Agent path not found: {agentPath}");
                return CreateValidationResult(false, errors, warnings, securityIssues, performanceConcerns, compatibilityIssues);
            }

            // Try to load agent definition
            AgentDefinition? definition = null;
            try
            {
                definition = await LoadAgentDefinitionAsync(agentPath, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load agent definition: {ex.Message}");
                return CreateValidationResult(false, errors, warnings, securityIssues, performanceConcerns, compatibilityIssues);
            }

            // Validate definition fields
            ValidateAgentDefinition(definition, errors, warnings);

            // Check assembly validity
            var assemblyPath = GetAgentAssemblyPath(agentPath, definition);
            if (!File.Exists(assemblyPath))
            {
                errors.Add($"Agent assembly not found: {assemblyPath}");
            }
            else
            {
                await ValidateAssembly(assemblyPath, errors, warnings, securityIssues, compatibilityIssues, cancellationToken);
            }

            // Security validation
            await ValidateAgentSecurity(definition, agentPath, securityIssues, warnings, cancellationToken);

            // Performance validation
            ValidatePerformanceCharacteristics(definition, performanceConcerns, warnings);

            // Compatibility validation
            await ValidateCompatibility(definition, assemblyPath, compatibilityIssues, warnings, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add($"Validation error: {ex.Message}");
        }

        var isValid = errors.Count == 0 && securityIssues.Count == 0;
        return CreateValidationResult(isValid, errors, warnings, securityIssues, performanceConcerns, compatibilityIssues);
    }

    public Task<IEnumerable<AgentDefinition>> GetLoadedAgentsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = _loadedAgents.Values.Select(a => a.Definition).ToList();
        return Task.FromResult<IEnumerable<AgentDefinition>>(definitions);
    }

    public Task<AgentDefinition?> GetLoadedAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _loadedAgents.TryGetValue(agentId, out var loadedAgent);
        return Task.FromResult(loadedAgent?.Definition);
    }

    public Task<Assembly?> GetAgentAssemblyAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _loadedAgents.TryGetValue(agentId, out var loadedAgent);
        return Task.FromResult(loadedAgent?.Assembly);
    }

    public bool IsAgentLoaded(string agentId)
    {
        return _loadedAgents.ContainsKey(agentId);
    }

    public async Task<IEnumerable<AgentCapability>> GetAgentCapabilitiesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (!_loadedAgents.TryGetValue(agentId, out var loadedAgent))
            return Enumerable.Empty<AgentCapability>();

        try
        {
            // Use reflection to discover capabilities from the assembly
            var capabilityTypes = loadedAgent.Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes<CapabilityAttribute>().Any())
                .ToList();

            var capabilities = new List<AgentCapability>();
            foreach (var type in capabilityTypes)
            {
                var attr = type.GetCustomAttribute<CapabilityAttribute>();
                if (attr != null)
                {
                    var capability = new AgentCapability
                    {
                        Id = attr.Id,
                        Name = attr.Name,
                        Version = attr.Version ?? loadedAgent.Definition.Version,
                        Description = attr.Description,
                        Category = attr.Category,
                        Tags = attr.Tags ?? Array.Empty<string>(),
                        RequiredPermissions = attr.RequiredPermissions ?? Array.Empty<string>()
                    };

                    capabilities.Add(capability);
                }
            }

            return capabilities;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover capabilities for agent {AgentId}", agentId);
            return Enumerable.Empty<AgentCapability>();
        }
    }

    public async Task<IEnumerable<CapabilityRegistryEntry>> RegisterAgentCapabilitiesAsync(string agentId, string? instanceId = null, CancellationToken cancellationToken = default)
    {
        var capabilities = await GetAgentCapabilitiesAsync(agentId, cancellationToken);
        var registryEntries = new List<CapabilityRegistryEntry>();

        foreach (var capability in capabilities)
        {
            try
            {
                var entry = await _capabilityRegistry.RegisterCapabilityAsync(capability, agentId, instanceId, cancellationToken: cancellationToken);
                registryEntries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register capability {CapabilityId} for agent {AgentId}", capability.Id, agentId);
            }
        }

        return registryEntries;
    }

    public async Task UnregisterAgentCapabilitiesAsync(string agentId, string? instanceId = null, CancellationToken cancellationToken = default)
    {
        var capabilities = await GetAgentCapabilitiesAsync(agentId, cancellationToken);

        foreach (var capability in capabilities)
        {
            try
            {
                await _capabilityRegistry.UnregisterCapabilityAsync(capability.Id, agentId, instanceId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister capability {CapabilityId} for agent {AgentId}", capability.Id, agentId);
            }
        }
    }

    public AgentLoadContext? GetLoadContext(string agentId)
    {
        _loadedAgents.TryGetValue(agentId, out var loadedAgent);
        return loadedAgent?.LoadContext;
    }

    private async Task<AgentDefinition> LoadAgentDefinitionAsync(string agentPath, CancellationToken cancellationToken)
    {
        string manifestPath;

        if (Directory.Exists(agentPath))
        {
            manifestPath = Path.Combine(agentPath, "agent.json");
        }
        else if (Path.GetExtension(agentPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            manifestPath = agentPath;
        }
        else
        {
            throw new InvalidOperationException($"Cannot determine agent manifest path from: {agentPath}");
        }

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Agent manifest not found: {manifestPath}");

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var definition = JsonSerializer.Deserialize<AgentDefinition>(json);

        if (definition == null)
            throw new InvalidOperationException("Failed to deserialize agent definition");

        return definition;
    }

    private PluginLoader CreatePluginLoader(string agentPath, AgentDefinition definition)
    {
        var assemblyPath = GetAgentAssemblyPath(agentPath, definition);

        var loader = PluginLoader.CreateFromAssemblyFile(
            assemblyPath,
            config =>
            {
                config.PreferSharedTypes = true;
                config.IsUnloadable = true;
                config.LoadInMemory = false;
            });

        return loader;
    }

    private string GetAgentAssemblyPath(string agentPath, AgentDefinition definition)
    {
        if (Directory.Exists(agentPath))
        {
            return Path.Combine(agentPath, definition.EntryPoint);
        }
        else
        {
            var directory = Path.GetDirectoryName(agentPath) ?? throw new InvalidOperationException("Invalid agent path");
            return Path.Combine(directory, definition.EntryPoint);
        }
    }

    private void ValidateAgentDefinition(AgentDefinition definition, List<string> errors, List<string> warnings)
    {
        // Required fields validation
        if (string.IsNullOrWhiteSpace(definition.Id))
            errors.Add("Agent ID is required");

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("Agent name is required");

        if (string.IsNullOrWhiteSpace(definition.Version))
            errors.Add("Agent version is required");

        if (string.IsNullOrWhiteSpace(definition.EntryPoint))
            errors.Add("Agent entry point is required");

        // Version format validation
        if (!Version.TryParse(definition.Version, out _))
            errors.Add($"Invalid version format: {definition.Version}");

        // ID format validation (should be valid identifier)
        if (!IsValidIdentifier(definition.Id))
            errors.Add($"Invalid agent ID format: {definition.Id}");

        // Resource requirements validation
        if (definition.Resources.MinCpuCores < 1)
            errors.Add("Minimum CPU cores must be at least 1");

        if (definition.Resources.MinMemoryMb < 64)
            warnings.Add("Minimum memory requirement is very low (< 64MB)");

        if (definition.Resources.TimeoutSeconds < 30)
            warnings.Add("Execution timeout is very short (< 30 seconds)");
    }

    private async Task ValidateAssembly(string assemblyPath, List<string> errors, List<string> warnings,
        List<string> securityIssues, List<string> compatibilityIssues, CancellationToken cancellationToken)
    {
        try
        {
            // Check if assembly can be loaded
            var assembly = Assembly.LoadFrom(assemblyPath);

            // Check for required interfaces or base classes
            var hasAgentInterface = assembly.GetTypes()
                .Any(t => t.GetInterfaces().Any(i => i.Name.Contains("IAgent")));

            if (!hasAgentInterface)
                warnings.Add("Assembly does not appear to implement standard agent interfaces");

            // Check for potentially dangerous APIs
            var dangerousApis = new[]
            {
                "System.IO.File",
                "System.Diagnostics.Process",
                "System.Net.Http",
                "System.Reflection.Assembly"
            };

            foreach (var api in dangerousApis)
            {
                var usesApi = assembly.GetTypes()
                    .SelectMany(t => t.GetMethods())
                    .Any(m => m.GetParameters().Any(p => p.ParameterType.FullName?.Contains(api) == true));

                if (usesApi)
                    securityIssues.Add($"Assembly uses potentially dangerous API: {api}");
            }

            // Check .NET version compatibility
            var targetFramework = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
            if (targetFramework != null)
            {
                if (!targetFramework.FrameworkName.Contains("net8.0") &&
                    !targetFramework.FrameworkName.Contains("net7.0"))
                {
                    compatibilityIssues.Add($"Assembly targets unsupported framework: {targetFramework.FrameworkName}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to validate assembly: {ex.Message}");
        }
    }

    private async Task ValidateAgentSecurity(AgentDefinition definition, string agentPath,
        List<string> securityIssues, List<string> warnings, CancellationToken cancellationToken)
    {
        // Check trust level requirements
        if (definition.Security.TrustLevel == TrustLevel.Full)
        {
            securityIssues.Add("Agent requires full trust level - this poses significant security risks");
        }

        // Check network access requirements
        if (definition.Resources.NetworkAccess == NetworkAccessLevel.Full)
        {
            warnings.Add("Agent requires full network access");
        }

        // Check file system access requirements
        if (definition.Resources.FileSystemAccess == FileSystemAccessLevel.Full)
        {
            securityIssues.Add("Agent requires full file system access - this poses security risks");
        }

        // Comprehensive security validation including code signing
        if (definition.Security.RequireCodeSigning || _configuration.Security.RequireCodeSigning)
        {
            try
            {
                var assemblyPath = GetAgentAssemblyPath(agentPath, definition);
                var securityResult = await _securityValidator.ValidateAgentSecurityAsync(assemblyPath, definition, cancellationToken);

                // Add security issues to the main validation result
                securityIssues.AddRange(securityResult.SecurityIssues);
                warnings.AddRange(securityResult.Warnings);

                _logger.LogInformation("Security validation completed for agent {AgentId}. Secure: {IsSecure}, Issues: {IssueCount}",
                    definition.Id, securityResult.IsSecure, securityResult.SecurityIssues.Count);

                // Fail validation if security validation fails in strict mode
                if (!securityResult.IsSecure && _configuration.Security.ValidationMode == SecurityValidationMode.Strict)
                {
                    securityIssues.Add("Agent failed security validation in strict mode");
                }
            }
            catch (SecurityException secEx)
            {
                _logger.LogError(secEx, "Security validation failed for agent {AgentId}", definition.Id);
                securityIssues.Add($"Security validation error: {secEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during security validation for agent {AgentId}", definition.Id);
                securityIssues.Add($"Security validation system error: {ex.Message}");
            }
        }

        // Check for suspicious capabilities
        var suspiciousCapabilities = new[] { "system.execute", "file.write", "network.connect" };
        foreach (var capability in definition.Capabilities.Intersect(suspiciousCapabilities))
        {
            warnings.Add($"Agent requests potentially dangerous capability: {capability}");
        }
    }

    private void ValidatePerformanceCharacteristics(AgentDefinition definition,
        List<string> performanceConcerns, List<string> warnings)
    {
        // Check resource requirements
        if (definition.Resources.MinMemoryMb > 1024)
        {
            performanceConcerns.Add($"Agent requires high memory: {definition.Resources.MinMemoryMb}MB");
        }

        if (definition.Resources.MinCpuCores > Environment.ProcessorCount)
        {
            performanceConcerns.Add($"Agent requires more CPU cores ({definition.Resources.MinCpuCores}) than available ({Environment.ProcessorCount})");
        }

        if (definition.Resources.TimeoutSeconds > 3600)
        {
            warnings.Add("Agent has very long execution timeout (> 1 hour)");
        }
    }

    private async Task ValidateCompatibility(AgentDefinition definition, string assemblyPath,
        List<string> compatibilityIssues, List<string> warnings, CancellationToken cancellationToken)
    {
        // Check dependencies
        foreach (var dependency in definition.Dependencies)
        {
            if (!IsAgentLoaded(dependency.Name) && dependency.Type == DependencyType.Agent)
            {
                if (!dependency.Optional)
                {
                    compatibilityIssues.Add($"Required agent dependency not available: {dependency.Name}");
                }
                else
                {
                    warnings.Add($"Optional agent dependency not available: {dependency.Name}");
                }
            }
        }

        // Check platform compatibility
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            warnings.Add("Agent may not be compatible with non-Windows platforms");
        }
    }

    private AgentValidationResult CreateValidationResult(bool isValid, List<string> errors, List<string> warnings,
        List<string> securityIssues, List<string> performanceConcerns, List<string> compatibilityIssues)
    {
        return new AgentValidationResult
        {
            IsValid = isValid,
            Errors = errors,
            Warnings = warnings,
            SecurityIssues = securityIssues,
            PerformanceConcerns = performanceConcerns,
            CompatibilityIssues = compatibilityIssues,
            ValidatorVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        };
    }

    private static bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;
        if (!char.IsLetter(identifier[0]) && identifier[0] != '_') return false;

        return identifier.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-');
    }

    private void OnAgentLoaded(AgentDefinition agent, AgentLoadContext loadContext)
    {
        AgentLoaded?.Invoke(this, new AgentLoadedEventArgs
        {
            Agent = agent,
            LoadContext = loadContext
        });
    }

    private void OnAgentUnloaded(string agentId, bool wasForced)
    {
        AgentUnloaded?.Invoke(this, new AgentUnloadedEventArgs
        {
            AgentId = agentId,
            WasForced = wasForced
        });
    }

    private void OnAgentLoadFailed(string agentPath, Exception error, AgentValidationResult? validationResult)
    {
        AgentLoadFailed?.Invoke(this, new AgentLoadFailedEventArgs
        {
            AgentPath = agentPath,
            Error = error,
            ValidationResult = validationResult
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _loadSemaphore?.Dispose();

        // Unload all agents
        var loadedAgentIds = _loadedAgents.Keys.ToList();
        foreach (var agentId in loadedAgentIds)
        {
            try
            {
                UnloadAgentAsync(agentId, force: true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unload agent {AgentId} during dispose", agentId);
            }
        }

        // Dispose plugin loaders
        foreach (var loader in _pluginLoaders.Values)
        {
            try
            {
                loader?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose plugin loader during cleanup");
            }
        }

        _disposed = true;
    }

    /// <summary>
    /// Represents a loaded agent with its context
    /// </summary>
    private class LoadedAgent
    {
        public required AgentDefinition Definition { get; init; }
        public required Assembly Assembly { get; init; }
        public required PluginLoader Loader { get; init; }
        public required AgentLoadContext LoadContext { get; init; }
    }
}

/// <summary>
/// Attribute to mark agent capabilities
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CapabilityAttribute : Attribute
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public string[]? Tags { get; init; }
    public string[]? RequiredPermissions { get; init; }
}