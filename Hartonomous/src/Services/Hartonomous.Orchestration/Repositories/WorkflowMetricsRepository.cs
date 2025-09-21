/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow metrics repository - a focused, purpose-built class.
 * Features performance analytics and execution statistics with clean separation of concerns.
 */

using Dapper;
using Hartonomous.Core.Configuration;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hartonomous.Orchestration.Repositories;

/// <summary>
/// Purpose-built repository for workflow metrics and analytics
/// </summary>
public class WorkflowMetricsRepository
{
    private readonly string _connectionString;
    private readonly ILogger<WorkflowMetricsRepository> _logger;

    public WorkflowMetricsRepository(IOptions<SqlServerOptions> sqlOptions, ILogger<WorkflowMetricsRepository> logger)
    {
        _connectionString = sqlOptions.Value.ConnectionString;
        _logger = logger;
    }

    public async Task<WorkflowExecutionStatsDto> GetWorkflowStatsAsync(Guid workflowId, string userId,
        DateTime? fromDate = null, DateTime? toDate = null)
    {
        var whereClause = "WHERE WorkflowId = @WorkflowId AND UserId = @UserId";
        var parameters = new DynamicParameters();
        parameters.Add("WorkflowId", workflowId);
        parameters.Add("UserId", userId);

        if (fromDate.HasValue)
        {
            whereClause += " AND StartedAt >= @FromDate";
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereClause += " AND StartedAt <= @ToDate";
            parameters.Add("ToDate", toDate.Value);
        }

        var sql = $@"
            SELECT
                COUNT(*) as TotalExecutions,
                SUM(CASE WHEN Status = @CompletedStatus THEN 1 ELSE 0 END) as SuccessfulExecutions,
                SUM(CASE WHEN Status = @FailedStatus THEN 1 ELSE 0 END) as FailedExecutions,
                SUM(CASE WHEN Status IN (@RunningStatus, @PendingStatus, @PausedStatus) THEN 1 ELSE 0 END) as RunningExecutions,
                AVG(CASE WHEN CompletedAt IS NOT NULL THEN DATEDIFF(SECOND, StartedAt, CompletedAt) ELSE NULL END) as AverageExecutionTime,
                MAX(StartedAt) as LastExecution
            FROM dbo.WorkflowExecutions
            {whereClause};";

        parameters.Add("CompletedStatus", (int)DTOs.WorkflowExecutionStatus.Completed);
        parameters.Add("FailedStatus", (int)DTOs.WorkflowExecutionStatus.Failed);
        parameters.Add("RunningStatus", (int)DTOs.WorkflowExecutionStatus.Running);
        parameters.Add("PendingStatus", (int)DTOs.WorkflowExecutionStatus.Pending);
        parameters.Add("PausedStatus", (int)DTOs.WorkflowExecutionStatus.Paused);

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstAsync(sql, parameters);

        var totalExecutions = result.TotalExecutions;
        var successfulExecutions = result.SuccessfulExecutions;
        var successRate = totalExecutions > 0 ? (double)successfulExecutions / totalExecutions * 100 : 0;

        return new WorkflowExecutionStatsDto(
            totalExecutions,
            successfulExecutions,
            result.FailedExecutions,
            result.RunningExecutions,
            result.AverageExecutionTime ?? 0,
            successRate,
            result.LastExecution,
            new List<ExecutionTrendDataPoint>()
        );
    }

    public async Task<bool> RecordExecutionMetricAsync(Guid executionId, string metricName, double value,
        string? unit = null, Dictionary<string, string>? tags = null)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowExecutionMetrics (MetricsId, ExecutionId, MetricName, MetricType, MetricValue, Unit, Timestamp, TagsJson)
            VALUES (@MetricsId, @ExecutionId, @MetricName, @MetricType, @MetricValue, @Unit, @Timestamp, @TagsJson);";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            MetricsId = Guid.NewGuid(),
            ExecutionId = executionId,
            MetricName = metricName,
            MetricType = "gauge",
            MetricValue = value,
            Unit = unit,
            Timestamp = DateTime.UtcNow,
            TagsJson = tags != null ? JsonSerializer.Serialize(tags) : null
        });

        return rowsAffected > 0;
    }

    public async Task<Dictionary<string, double>> GetExecutionMetricsAsync(Guid executionId)
    {
        const string sql = @"
            SELECT MetricName, MetricValue
            FROM dbo.WorkflowExecutionMetrics
            WHERE ExecutionId = @ExecutionId
            ORDER BY Timestamp DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<(string MetricName, double MetricValue)>(sql, new { ExecutionId = executionId });

        return results.ToDictionary(r => r.MetricName, r => r.MetricValue);
    }

    public async Task<List<ExecutionTrendDataPoint>> GetExecutionTrendAsync(Guid workflowId, string userId, int dayCount = 30)
    {
        const string sql = @"
            SELECT
                CAST(StartedAt AS DATE) as Date,
                COUNT(*) as ExecutionCount,
                AVG(CASE WHEN CompletedAt IS NOT NULL THEN DATEDIFF(SECOND, StartedAt, CompletedAt) ELSE NULL END) as AvgDuration,
                SUM(CASE WHEN Status = @CompletedStatus THEN 1 ELSE 0 END) as SuccessCount
            FROM dbo.WorkflowExecutions
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId
                AND StartedAt >= DATEADD(DAY, -@DayCount, GETUTCDATE())
            GROUP BY CAST(StartedAt AS DATE)
            ORDER BY Date DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new
        {
            WorkflowId = workflowId,
            UserId = userId,
            DayCount = dayCount,
            CompletedStatus = (int)DTOs.WorkflowExecutionStatus.Completed
        });

        return results.Select(r => new ExecutionTrendDataPoint(
            r.Date,
            r.ExecutionCount,
            r.AvgDuration ?? 0,
            r.SuccessCount
        )).ToList();
    }
}