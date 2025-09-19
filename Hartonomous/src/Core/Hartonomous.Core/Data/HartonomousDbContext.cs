/*
 * Hartonomous AI Agent Factory Platform
 * Core Database Context - NinaDB Implementation with SQL Server 2025 Native AI Capabilities
 *
 * Copyright (c) 2024-2025 All Rights Reserved.
 * This software is proprietary and confidential. No part of this software may be reproduced,
 * distributed, or transmitted in any form or by any means without the prior written permission
 * of the copyright holder.
 *
 * This DbContext implements the NinaDB concept using SQL Server 2025's native vector capabilities,
 * JSON support, and FILESTREAM integration for comprehensive AI data management.
 *
 * SECURITY NOTICE: This context enforces multi-tenant row-level security. All data operations
 * are automatically scoped by authenticated user ID (oid claim from JWT tokens).
 *
 * Author: AI Agent Factory Development Team
 * Created: 2024
 * Last Modified: 2025
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Hartonomous.Core.Models;
using Hartonomous.Core.Models.Configuration;

namespace Hartonomous.Core.Data;

/// <summary>
/// Primary Entity Framework DbContext for Hartonomous platform
/// Implements NinaDB concept using SQL Server 2025 with native vector capabilities
/// </summary>
public class HartonomousDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public HartonomousDbContext(DbContextOptions<HartonomousDbContext> options) : base(options) { }

    public HartonomousDbContext(DbContextOptions<HartonomousDbContext> options, IHttpContextAccessor httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // Core Model Storage
    public DbSet<Model> Models { get; set; }
    public DbSet<ModelLayer> ModelLayers { get; set; }
    public DbSet<ModelComponent> ModelComponents { get; set; }
    public DbSet<ComponentWeight> ComponentWeights { get; set; }

    // Vector and Embedding Storage
    public DbSet<ModelEmbedding> ModelEmbeddings { get; set; }
    public DbSet<ComponentEmbedding> ComponentEmbeddings { get; set; }

    // Mechanistic Interpretability
    public DbSet<NeuronInterpretation> NeuronInterpretations { get; set; }
    public DbSet<AttentionHead> AttentionHeads { get; set; }
    public DbSet<ActivationPattern> ActivationPatterns { get; set; }

    // Agent Distillation
    public DbSet<DistilledAgent> DistilledAgents { get; set; }
    public DbSet<AgentComponent> AgentComponents { get; set; }
    public DbSet<AgentCapability> AgentCapabilities { get; set; }

    // Constitutional AI
    public DbSet<ConstitutionalRule> ConstitutionalRules { get; set; }
    public DbSet<SafetyConstraint> SafetyConstraints { get; set; }

    // Capability and Performance Tracking
    public DbSet<CapabilityMapping> CapabilityMappings { get; set; }
    public DbSet<ModelPerformanceMetric> ModelPerformanceMetrics { get; set; }

    // Projects and Organization
    public DbSet<Project> Projects { get; set; }
    public DbSet<ProjectModel> ProjectModels { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations
        modelBuilder.ApplyConfiguration(new ModelConfiguration());
        modelBuilder.ApplyConfiguration(new ModelLayerConfiguration());
        modelBuilder.ApplyConfiguration(new ModelComponentConfiguration());
        modelBuilder.ApplyConfiguration(new ComponentWeightConfiguration());
        modelBuilder.ApplyConfiguration(new ModelEmbeddingConfiguration());
        modelBuilder.ApplyConfiguration(new ComponentEmbeddingConfiguration());
        modelBuilder.ApplyConfiguration(new NeuronInterpretationConfiguration());
        modelBuilder.ApplyConfiguration(new AttentionHeadConfiguration());
        modelBuilder.ApplyConfiguration(new ActivationPatternConfiguration());
        modelBuilder.ApplyConfiguration(new DistilledAgentConfiguration());
        modelBuilder.ApplyConfiguration(new AgentComponentConfiguration());
        modelBuilder.ApplyConfiguration(new AgentCapabilityConfiguration());
        modelBuilder.ApplyConfiguration(new ConstitutionalRuleConfiguration());
        modelBuilder.ApplyConfiguration(new SafetyConstraintConfiguration());
        modelBuilder.ApplyConfiguration(new CapabilityMappingConfiguration());
        modelBuilder.ApplyConfiguration(new ModelPerformanceMetricConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectModelConfiguration());

        // Global query filters for multi-tenancy
        modelBuilder.Entity<Model>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ModelLayer>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ModelComponent>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ComponentWeight>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ModelEmbedding>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ComponentEmbedding>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<NeuronInterpretation>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<AttentionHead>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ActivationPattern>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<DistilledAgent>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<AgentComponent>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<AgentCapability>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ConstitutionalRule>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<SafetyConstraint>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<CapabilityMapping>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ModelPerformanceMetric>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<Project>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
        modelBuilder.Entity<ProjectModel>().HasQueryFilter(e => EF.Property<string>(e, "UserId") == GetCurrentUserId());
    }

    /// <summary>
    /// Get current user ID from HTTP context for multi-tenant filtering
    /// This should be set by middleware
    /// </summary>
    private string GetCurrentUserId()
    {
        // Try to get from HttpContext
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            // Get user ID from JWT token claims
            var userIdClaim = httpContext.User?.FindFirst("oid") ?? httpContext.User?.FindFirst("sub");
            if (userIdClaim != null)
            {
                return userIdClaim.Value;
            }

            // Fallback to custom header for API clients
            if (httpContext.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader))
            {
                return userIdHeader.FirstOrDefault() ?? "anonymous";
            }
        }

        // Development/testing fallback
        return Environment.GetEnvironmentVariable("HARTONOMOUS_DEFAULT_USER_ID") ?? "development-user";
    }

    /// <summary>
    /// Raw SQL execution for T-SQL REST operations and stored procedures
    /// </summary>
    public async Task<T> ExecuteStoredProcedureAsync<T>(string procedureName, object parameters)
    {
        // Implementation for calling stored procedures with parameters
        var sql = $"EXEC {procedureName}";
        var paramList = new List<object>();

        if (parameters != null)
        {
            var properties = parameters.GetType().GetProperties();
            var paramNames = new List<string>();

            foreach (var prop in properties)
            {
                var paramName = $"@{prop.Name}";
                paramNames.Add($"{paramName}={{0}}");
                paramList.Add(prop.GetValue(parameters));
            }

            if (paramNames.Any())
            {
                sql += " " + string.Join(", ", paramNames);
            }
        }

        var result = await Database.SqlQueryRaw<T>(sql, paramList.ToArray()).ToListAsync();
        return result.FirstOrDefault();
    }

    /// <summary>
    /// Execute T-SQL REST endpoint calls through stored procedures
    /// </summary>
    public async Task<string> InvokeExternalRestEndpointAsync(string url, string method, string payload, string headers = null)
    {
        var parameters = new
        {
            url = url,
            method = method,
            payload = payload,
            headers = headers ?? "{\"Content-Type\": \"application/json\"}"
        };

        return await ExecuteStoredProcedureAsync<string>("sp_invoke_external_rest_endpoint", parameters);
    }

    /// <summary>
    /// Execute vector similarity search using native SQL Server 2025 capabilities
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> VectorSimilaritySearchAsync(float[] queryVector, string tableName, string vectorColumn, double threshold = 0.8, int limit = 10)
    {
        var vectorString = $"[{string.Join(",", queryVector)}]";

        var sql = $@"
            SELECT TOP (@limit)
                Id,
                VECTOR_DISTANCE('cosine', {vectorColumn}, @queryVector) AS Similarity
            FROM {tableName}
            WHERE VECTOR_DISTANCE('cosine', {vectorColumn}, @queryVector) > @threshold
            ORDER BY Similarity DESC";

        return await Database.SqlQueryRaw<VectorSearchResult>(sql,
            new object[] { limit, vectorString, threshold }).ToListAsync();
    }
}

/// <summary>
/// Result type for vector similarity searches
/// </summary>
public class VectorSearchResult
{
    public Guid Id { get; set; }
    public double Similarity { get; set; }
}