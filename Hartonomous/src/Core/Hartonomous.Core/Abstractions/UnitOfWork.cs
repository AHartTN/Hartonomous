using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using Hartonomous.Core.Configuration;

namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Unit of work implementation for transaction management
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly SqlServerOptions _sqlOptions;
    private readonly IServiceProvider _serviceProvider;
    private IDbConnection? _connection;
    private IDbTransaction? _transaction;
    private readonly Dictionary<Type, object> _repositories = new();
    private bool _disposed = false;

    public UnitOfWork(IOptions<SqlServerOptions> sqlOptions, IServiceProvider serviceProvider)
    {
        _sqlOptions = sqlOptions.Value;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Get repository for specific entity type
    /// </summary>
    public IRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        var entityType = typeof(TEntity);
        if (_repositories.TryGetValue(entityType, out var existingRepo))
        {
            return (IRepository<TEntity, TKey>)existingRepo;
        }

        // Create repository with shared connection
        var repository = CreateRepositoryWithConnection<TEntity, TKey>();
        _repositories[entityType] = repository;
        return repository;
    }

    /// <summary>
    /// Begin new transaction
    /// </summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
            throw new InvalidOperationException("Transaction already started");

        await EnsureConnectionAsync();
        _transaction = await Task.Run(() => _connection!.BeginTransaction(), cancellationToken);
    }

    /// <summary>
    /// Commit current transaction
    /// </summary>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No transaction to commit");

        try
        {
            await Task.Run(() => _transaction.Commit(), cancellationToken);
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    /// <summary>
    /// Rollback current transaction
    /// </summary>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No transaction to rollback");

        try
        {
            await Task.Run(() => _transaction.Rollback(), cancellationToken);
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    /// <summary>
    /// Save all changes (no-op for repository pattern, but useful for tracking)
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // In repository pattern, changes are saved immediately
        // This method can be used for batching or change tracking if needed
        await Task.CompletedTask;
        return 0;
    }

    /// <summary>
    /// Ensure database connection is established
    /// </summary>
    private async Task EnsureConnectionAsync()
    {
        if (_connection == null)
        {
            _connection = new SqlConnection(_sqlOptions.ConnectionString);
            await Task.Run(() => _connection.Open());
        }
    }

    /// <summary>
    /// Create repository with shared connection for transaction support
    /// </summary>
    private IRepository<TEntity, TKey> CreateRepositoryWithConnection<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        // For now, return the standard repository
        // In a full implementation, you'd create a repository that uses the shared connection
        return _serviceProvider.GetRequiredService<IRepository<TEntity, TKey>>();
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                _transaction?.Rollback();
            }
            catch
            {
                // Ignore rollback errors during disposal
            }
            finally
            {
                _transaction?.Dispose();
                _connection?.Dispose();
                _repositories.Clear();
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Agent factory implementation for creating domain-specific agents
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Func<object, Task<IAgent>>> _agentFactories = new();
    private readonly Dictionary<string, Type> _agentTypes = new();

    public AgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        RegisterBuiltInAgentTypes();
    }

    /// <summary>
    /// Create agent by type and configuration
    /// </summary>
    public async Task<TAgent> CreateAgentAsync<TAgent>(string agentType, object configuration)
        where TAgent : class, IAgent
    {
        if (!_agentFactories.TryGetValue(agentType, out var factory))
        {
            throw new ArgumentException($"Unknown agent type: {agentType}");
        }

        var agent = await factory(configuration);
        if (agent is not TAgent typedAgent)
        {
            throw new InvalidOperationException($"Agent type {agentType} does not implement {typeof(TAgent).Name}");
        }

        await typedAgent.InitializeAsync(configuration as Dictionary<string, object> ?? new Dictionary<string, object>());
        return typedAgent;
    }

    /// <summary>
    /// Create agent from template
    /// </summary>
    public async Task<TAgent> CreateAgentFromTemplateAsync<TAgent>(string templateName, Dictionary<string, object> parameters)
        where TAgent : class, IAgent
    {
        // Load template configuration
        var templateConfig = await LoadAgentTemplateAsync(templateName, parameters);

        // Determine agent type from template
        var agentType = templateConfig.ContainsKey("AgentType")
            ? templateConfig["AgentType"].ToString()!
            : templateName;

        return await CreateAgentAsync<TAgent>(agentType, templateConfig);
    }

    /// <summary>
    /// Register agent type with factory
    /// </summary>
    public void RegisterAgentType<TAgent>(string agentType, Func<object, Task<TAgent>> factory)
        where TAgent : class, IAgent
    {
        _agentFactories[agentType] = async config => await factory(config);
        _agentTypes[agentType] = typeof(TAgent);
    }

    /// <summary>
    /// Register built-in agent types
    /// </summary>
    private void RegisterBuiltInAgentTypes()
    {
        // Chess agent
        RegisterAgentType<ChessAgent>("chess", async config =>
        {
            var agent = ActivatorUtilities.CreateInstance<ChessAgent>(_serviceProvider);
            return agent;
        });

        // Code review agent
        RegisterAgentType<CodeReviewAgent>("code-review", async config =>
        {
            var agent = ActivatorUtilities.CreateInstance<CodeReviewAgent>(_serviceProvider);
            return agent;
        });

        // D&D Dungeon Master agent
        RegisterAgentType<DungeonMasterAgent>("dungeon-master", async config =>
        {
            var agent = ActivatorUtilities.CreateInstance<DungeonMasterAgent>(_serviceProvider);
            return agent;
        });

        // Generic agent for custom types
        RegisterAgentType<GenericAgent>("generic", async config =>
        {
            var agent = ActivatorUtilities.CreateInstance<GenericAgent>(_serviceProvider);
            return agent;
        });
    }

    /// <summary>
    /// Load agent template configuration
    /// </summary>
    private async Task<Dictionary<string, object>> LoadAgentTemplateAsync(string templateName, Dictionary<string, object> parameters)
    {
        // This would load from database, file system, or configuration
        // For now, return basic template
        var template = new Dictionary<string, object>
        {
            ["AgentType"] = templateName,
            ["Name"] = parameters.GetValueOrDefault("Name", $"{templateName}-agent"),
            ["Description"] = parameters.GetValueOrDefault("Description", $"Agent created from {templateName} template"),
            ["Capabilities"] = parameters.GetValueOrDefault("Capabilities", new string[0]),
            ["Configuration"] = parameters
        };

        return await Task.FromResult(template);
    }
}

/// <summary>
/// Base agent implementation
/// </summary>
public abstract class BaseAgent : IAgent
{
    protected BaseAgent(string id, string type, string userId)
    {
        Id = id;
        Type = type;
        UserId = userId;
        CreatedDate = DateTime.UtcNow;
        Configuration = new Dictionary<string, object>();
    }

    public string Id { get; }
    public string Type { get; }
    public string UserId { get; }
    public DateTime CreatedDate { get; }
    public Dictionary<string, object> Configuration { get; private set; }

    public virtual async Task InitializeAsync(Dictionary<string, object> configuration)
    {
        Configuration = configuration ?? new Dictionary<string, object>();
        await OnInitializeAsync(configuration);
    }

    public abstract Task<object> ExecuteAsync(string capability, object input);
    public abstract Task<IEnumerable<string>> GetCapabilitiesAsync();

    public virtual async Task<bool> ValidateAsync()
    {
        var capabilities = await GetCapabilitiesAsync();
        return capabilities.Any();
    }

    protected virtual async Task OnInitializeAsync(Dictionary<string, object> configuration)
    {
        await Task.CompletedTask;
    }
}

/// <summary>
/// Placeholder agent implementations for demonstration
/// </summary>
public class ChessAgent : BaseAgent
{
    public ChessAgent() : base(Guid.NewGuid().ToString(), "chess", "system") { }

    public override async Task<object> ExecuteAsync(string capability, object input)
    {
        return await Task.FromResult($"Chess agent executing {capability} with input: {input}");
    }

    public override async Task<IEnumerable<string>> GetCapabilitiesAsync()
    {
        return await Task.FromResult(new[] { "analyze-position", "suggest-move", "evaluate-game" });
    }
}

public class CodeReviewAgent : BaseAgent
{
    public CodeReviewAgent() : base(Guid.NewGuid().ToString(), "code-review", "system") { }

    public override async Task<object> ExecuteAsync(string capability, object input)
    {
        return await Task.FromResult($"Code review agent executing {capability} with input: {input}");
    }

    public override async Task<IEnumerable<string>> GetCapabilitiesAsync()
    {
        return await Task.FromResult(new[] { "review-code", "suggest-improvements", "check-security" });
    }
}

public class DungeonMasterAgent : BaseAgent
{
    public DungeonMasterAgent() : base(Guid.NewGuid().ToString(), "dungeon-master", "system") { }

    public override async Task<object> ExecuteAsync(string capability, object input)
    {
        return await Task.FromResult($"Dungeon Master agent executing {capability} with input: {input}");
    }

    public override async Task<IEnumerable<string>> GetCapabilitiesAsync()
    {
        return await Task.FromResult(new[] { "generate-story", "manage-npcs", "adjudicate-rules" });
    }
}

public class GenericAgent : BaseAgent
{
    public GenericAgent() : base(Guid.NewGuid().ToString(), "generic", "system") { }

    public override async Task<object> ExecuteAsync(string capability, object input)
    {
        return await Task.FromResult($"Generic agent executing {capability} with input: {input}");
    }

    public override async Task<IEnumerable<string>> GetCapabilitiesAsync()
    {
        var configuredCapabilities = Configuration.GetValueOrDefault("Capabilities", new string[0]) as string[];
        return await Task.FromResult(configuredCapabilities ?? new string[0]);
    }
}