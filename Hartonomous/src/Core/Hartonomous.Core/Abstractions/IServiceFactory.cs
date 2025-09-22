using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Repositories;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;

namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Generic service factory interface
/// </summary>
/// <typeparam name="TService">Service interface type</typeparam>
public interface IServiceFactory<TService> where TService : class
{
    /// <summary>
    /// Create service instance
    /// </summary>
    TService CreateService();

    /// <summary>
    /// Create service instance with specific configuration
    /// </summary>
    TService CreateService(string configurationKey);

    /// <summary>
    /// Create service instance with custom parameters
    /// </summary>
    TService CreateService(params object[] parameters);
}

/// <summary>
/// Generic service factory implementation
/// </summary>
/// <typeparam name="TService">Service interface type</typeparam>
/// <typeparam name="TImplementation">Service implementation type</typeparam>
public class ServiceFactory<TService, TImplementation> : IServiceFactory<TService>
    where TService : class
    where TImplementation : class, TService
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TService CreateService()
    {
        return _serviceProvider.GetRequiredService<TImplementation>();
    }

    public TService CreateService(string configurationKey)
    {
        // Use keyed services if available (.NET 8+)
        return _serviceProvider.GetKeyedService<TService>(configurationKey)
               ?? _serviceProvider.GetRequiredService<TImplementation>();
    }

    public TService CreateService(params object[] parameters)
    {
        return ActivatorUtilities.CreateInstance<TImplementation>(_serviceProvider, parameters);
    }
}

/// <summary>
/// Agent factory interface for creating domain-specific agents
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Create agent by type and configuration
    /// </summary>
    Task<TAgent> CreateAgentAsync<TAgent>(string agentType, object configuration)
        where TAgent : class, IAgent;

    /// <summary>
    /// Create agent from template
    /// </summary>
    Task<TAgent> CreateAgentFromTemplateAsync<TAgent>(string templateName, Dictionary<string, object> parameters)
        where TAgent : class, IAgent;

    /// <summary>
    /// Register agent type with factory
    /// </summary>
    void RegisterAgentType<TAgent>(string agentType, Func<object, Task<TAgent>> factory)
        where TAgent : class, IAgent;
}

/// <summary>
/// Base agent interface
/// </summary>
public interface IAgent
{
    string Id { get; }
    string Type { get; }
    string UserId { get; }
    DateTime CreatedDate { get; }
    Dictionary<string, object> Configuration { get; }

    /// <summary>
    /// Initialize agent with configuration
    /// </summary>
    Task InitializeAsync(Dictionary<string, object> configuration);

    /// <summary>
    /// Execute agent capability
    /// </summary>
    Task<object> ExecuteAsync(string capability, object input);

    /// <summary>
    /// Get available capabilities
    /// </summary>
    Task<IEnumerable<string>> GetCapabilitiesAsync();

    /// <summary>
    /// Validate agent configuration
    /// </summary>
    Task<bool> ValidateAsync();
}

/// <summary>
/// Repository factory implementation
/// </summary>
public class RepositoryFactory : IRepositoryFactory
{
    private readonly IServiceProvider _serviceProvider;

    public RepositoryFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IRepository<TEntity, TKey> CreateRepository<TEntity, TKey>()
        where TEntity : class, IEntityBase<TKey>
        where TKey : IEquatable<TKey>
    {
        // Return the generic repository implementation
        var repository = _serviceProvider.GetService<IRepository<TEntity, TKey>>() 
            ?? new GenericRepository<TEntity, TKey>(_serviceProvider.GetRequiredService<IOptions<SqlServerOptions>>());
        return repository;
    }

    public IRepository<TEntity, TKey> CreateRepository<TEntity, TKey>(string connectionString)
        where TEntity : class, IEntityBase<TKey>
        where TKey : IEquatable<TKey>
    {
        // Create repository with custom connection string
        return ActivatorUtilities.CreateInstance<GenericRepository<TEntity, TKey>>(
            _serviceProvider, connectionString);
    }
}

/// <summary>
/// Generic repository implementation for runtime entity types
/// </summary>
internal class GenericRepository<TEntity, TKey> : BaseRepository<TEntity, TKey>
    where TEntity : class, IEntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly string _customConnectionString;

    public GenericRepository(Microsoft.Extensions.Options.IOptions<SqlServerOptions> sqlOptions)
        : base(sqlOptions)
    {
        _customConnectionString = sqlOptions.Value.ConnectionString;
    }

    public GenericRepository(Microsoft.Extensions.Options.IOptions<SqlServerOptions> sqlOptions, string connectionString)
        : base(sqlOptions)
    {
        _customConnectionString = connectionString;
    }

    protected override string GetTableName()
    {
        // Use entity type name as table name by convention
        return typeof(TEntity).Name + "s";
    }

    protected override string GetSelectColumns()
    {
        // Use reflection to get all properties
        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name);
        return string.Join(", ", properties);
    }

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters()
    {
        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanWrite && p.Name != "Id" && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToArray();

        var columns = string.Join(", ", properties);
        var parameters = string.Join(", ", properties.Select(p => "@" + p));

        return (columns, parameters);
    }

    protected override string GetUpdateSetClause()
    {
        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanWrite && p.Name != "Id" && p.GetIndexParameters().Length == 0)
            .Select(p => $"{p.Name} = @{p.Name}")
            .ToArray();

        return string.Join(", ", properties);
    }

    protected override TEntity MapToEntity(dynamic row)
    {
        // Basic reflection-based mapping
        var entity = Activator.CreateInstance<TEntity>();
        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, p => p);

        var dict = (IDictionary<string, object>)row;
        foreach (var kvp in dict)
        {
            if (properties.TryGetValue(kvp.Key, out var property))
            {
                var value = kvp.Value == DBNull.Value ? null : kvp.Value;
                if (value != null && property.PropertyType != value.GetType())
                {
                    value = Convert.ChangeType(value, property.PropertyType);
                }
                property.SetValue(entity, value);
            }
        }

        return entity;
    }

    protected override object GetParameters(TEntity entity)
    {
        var properties = typeof(TEntity).GetProperties()
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, p => p.GetValue(entity));

        return properties;
    }
}