using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hartonomous.AgentClient.Models;

/// <summary>
/// Defines an agent's metadata, capabilities, and execution requirements
/// </summary>
public sealed record AgentDefinition
{
    /// <summary>
    /// Unique identifier for the agent
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Agent display name
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Agent version (semantic versioning)
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Agent description
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Agent author/publisher
    /// </summary>
    [JsonPropertyName("author")]
    public required string Author { get; init; }

    /// <summary>
    /// Agent type (e.g., CodeAnalyzer, Tester, Deployer)
    /// </summary>
    [JsonPropertyName("type")]
    public required AgentType Type { get; init; }

    /// <summary>
    /// List of capabilities this agent provides
    /// </summary>
    [JsonPropertyName("capabilities")]
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>
    /// Dependencies on other agents or services
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<AgentDependency> Dependencies { get; init; } = Array.Empty<AgentDependency>();

    /// <summary>
    /// Resource requirements for execution
    /// </summary>
    [JsonPropertyName("resources")]
    public required AgentResourceRequirements Resources { get; init; }

    /// <summary>
    /// Security configuration
    /// </summary>
    [JsonPropertyName("security")]
    public required AgentSecurityConfiguration Security { get; init; }

    /// <summary>
    /// Entry point assembly path
    /// </summary>
    [JsonPropertyName("entryPoint")]
    public required string EntryPoint { get; init; }

    /// <summary>
    /// Configuration schema for the agent
    /// </summary>
    [JsonPropertyName("configurationSchema")]
    public Dictionary<string, object>? ConfigurationSchema { get; init; }

    /// <summary>
    /// Tags for categorization and discovery
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// License information
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; init; }

    /// <summary>
    /// Homepage or documentation URL
    /// </summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    /// <summary>
    /// Repository URL
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; init; }

    /// <summary>
    /// Checksum for integrity verification
    /// </summary>
    [JsonPropertyName("checksum")]
    public string? Checksum { get; init; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Agent type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentType
{
    /// <summary>
    /// Code analysis and review agent
    /// </summary>
    CodeAnalyzer,

    /// <summary>
    /// Testing and quality assurance agent
    /// </summary>
    Tester,

    /// <summary>
    /// Deployment and infrastructure agent
    /// </summary>
    Deployer,

    /// <summary>
    /// Monitoring and observability agent
    /// </summary>
    Monitor,

    /// <summary>
    /// Security scanning and compliance agent
    /// </summary>
    Security,

    /// <summary>
    /// Documentation generation agent
    /// </summary>
    Documentation,

    /// <summary>
    /// Integration and workflow agent
    /// </summary>
    Integration,

    /// <summary>
    /// AI/ML model training and inference agent
    /// </summary>
    MachineLearning,

    /// <summary>
    /// General purpose utility agent
    /// </summary>
    Utility,

    /// <summary>
    /// Custom agent type
    /// </summary>
    Custom
}

/// <summary>
/// Agent dependency specification
/// </summary>
public sealed record AgentDependency
{
    /// <summary>
    /// Dependency name or agent ID
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Version constraint (semantic versioning)
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Whether this dependency is optional
    /// </summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; init; } = false;

    /// <summary>
    /// Dependency type
    /// </summary>
    [JsonPropertyName("type")]
    public DependencyType Type { get; init; } = DependencyType.Agent;
}

/// <summary>
/// Dependency type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DependencyType
{
    /// <summary>
    /// Another agent
    /// </summary>
    Agent,

    /// <summary>
    /// External service
    /// </summary>
    Service,

    /// <summary>
    /// System library or tool
    /// </summary>
    Library
}

/// <summary>
/// Agent resource requirements
/// </summary>
public sealed record AgentResourceRequirements
{
    /// <summary>
    /// Minimum CPU cores required
    /// </summary>
    [JsonPropertyName("minCpuCores")]
    public int MinCpuCores { get; init; } = 1;

    /// <summary>
    /// Maximum CPU cores that can be utilized
    /// </summary>
    [JsonPropertyName("maxCpuCores")]
    public int? MaxCpuCores { get; init; }

    /// <summary>
    /// Minimum memory in MB
    /// </summary>
    [JsonPropertyName("minMemoryMb")]
    public int MinMemoryMb { get; init; } = 256;

    /// <summary>
    /// Maximum memory in MB
    /// </summary>
    [JsonPropertyName("maxMemoryMb")]
    public int? MaxMemoryMb { get; init; }

    /// <summary>
    /// Minimum disk space in MB
    /// </summary>
    [JsonPropertyName("minDiskMb")]
    public int MinDiskMb { get; init; } = 100;

    /// <summary>
    /// Network access requirements
    /// </summary>
    [JsonPropertyName("networkAccess")]
    public NetworkAccessLevel NetworkAccess { get; init; } = NetworkAccessLevel.None;

    /// <summary>
    /// File system access requirements
    /// </summary>
    [JsonPropertyName("fileSystemAccess")]
    public FileSystemAccessLevel FileSystemAccess { get; init; } = FileSystemAccessLevel.Restricted;

    /// <summary>
    /// Process isolation level
    /// </summary>
    [JsonPropertyName("isolationLevel")]
    public IsolationLevel IsolationLevel { get; init; } = IsolationLevel.Process;

    /// <summary>
    /// Execution timeout in seconds
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; } = 300;
}

/// <summary>
/// Network access level enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkAccessLevel
{
    /// <summary>
    /// No network access
    /// </summary>
    None,

    /// <summary>
    /// Local network only
    /// </summary>
    Local,

    /// <summary>
    /// Internet access allowed
    /// </summary>
    Internet,

    /// <summary>
    /// Full network access
    /// </summary>
    Full
}

/// <summary>
/// File system access level enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileSystemAccessLevel
{
    /// <summary>
    /// No file system access
    /// </summary>
    None,

    /// <summary>
    /// Read-only access to designated directories
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Restricted access to working directory
    /// </summary>
    Restricted,

    /// <summary>
    /// Full file system access (admin only)
    /// </summary>
    Full
}

/// <summary>
/// Process isolation level enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IsolationLevel
{
    /// <summary>
    /// Run in same process (AppDomain isolation)
    /// </summary>
    AppDomain,

    /// <summary>
    /// Run in separate process
    /// </summary>
    Process,

    /// <summary>
    /// Run in container
    /// </summary>
    Container,

    /// <summary>
    /// Run in virtual machine
    /// </summary>
    VirtualMachine
}

/// <summary>
/// Agent security configuration
/// </summary>
public sealed record AgentSecurityConfiguration
{
    /// <summary>
    /// Trust level required to execute this agent
    /// </summary>
    [JsonPropertyName("trustLevel")]
    public TrustLevel TrustLevel { get; init; } = TrustLevel.Untrusted;

    /// <summary>
    /// Code signing verification required
    /// </summary>
    [JsonPropertyName("requireCodeSigning")]
    public bool RequireCodeSigning { get; init; } = true;

    /// <summary>
    /// Allowed capabilities
    /// </summary>
    [JsonPropertyName("allowedCapabilities")]
    public IReadOnlyList<string> AllowedCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Restricted APIs or namespaces
    /// </summary>
    [JsonPropertyName("restrictedApis")]
    public IReadOnlyList<string> RestrictedApis { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Security policies to enforce
    /// </summary>
    [JsonPropertyName("securityPolicies")]
    public IReadOnlyList<string> SecurityPolicies { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Trust level enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustLevel
{
    /// <summary>
    /// Untrusted - maximum restrictions
    /// </summary>
    Untrusted,

    /// <summary>
    /// Low trust - limited capabilities
    /// </summary>
    Low,

    /// <summary>
    /// Medium trust - standard capabilities
    /// </summary>
    Medium,

    /// <summary>
    /// High trust - extended capabilities
    /// </summary>
    High,

    /// <summary>
    /// Full trust - unrestricted access
    /// </summary>
    Full
}