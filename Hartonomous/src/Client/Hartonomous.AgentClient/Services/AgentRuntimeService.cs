using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.AgentClient.Services;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Agent runtime service providing process isolation and resource management
/// </summary>
public class AgentRuntimeService : IAgentRuntime, IDisposable
{
    private readonly ILogger<AgentRuntimeService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly AgentClientConfiguration _configuration;
    private readonly ICurrentUserService _currentUserService;
    private readonly ConcurrentDictionary<string, AgentInstance> _instances = new();
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly ConcurrentDictionary<string, PerformanceCounter[]> _performanceCounters = new();
    private readonly Timer _monitoringTimer;
    private readonly SemaphoreSlim _instanceSemaphore = new(1, 1);
    private bool _disposed;

    public AgentRuntimeService(
        ILogger<AgentRuntimeService> logger,
        IMetricsCollector metricsCollector,
        IOptions<AgentClientConfiguration> configuration,
        ICurrentUserService currentUserService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

        // Start monitoring timer
        _monitoringTimer = new Timer(MonitorInstances, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public event EventHandler<AgentInstanceEventArgs>? InstanceStatusChanged;
    public event EventHandler<AgentInstanceEventArgs>? InstanceMetricsUpdated;
    public event EventHandler<AgentInstanceErrorEventArgs>? InstanceError;

    public async Task<AgentInstance> CreateInstanceAsync(
        AgentDefinition definition,
        Dictionary<string, object>? configuration = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        userId ??= await _currentUserService.GetCurrentUserIdAsync(cancellationToken);
        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User not authenticated");

        await _instanceSemaphore.WaitAsync(cancellationToken);
        try
        {
            var instanceId = Guid.NewGuid().ToString();
            var workingDirectory = Path.Combine(_configuration.AgentWorkspacePath, instanceId);

            // Create working directory
            Directory.CreateDirectory(workingDirectory);

            var instance = new AgentInstance
            {
                InstanceId = instanceId,
                AgentId = definition.Id,
                Name = definition.Name,
                Version = definition.Version,
                Status = AgentInstanceStatus.Stopped,
                WorkingDirectory = workingDirectory,
                Configuration = configuration ?? new Dictionary<string, object>(),
                Environment = CreateEnvironmentVariables(definition, instanceId),
                UserId = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _instances.TryAdd(instanceId, instance);

            _logger.LogInformation("Created agent instance {InstanceId} for agent {AgentId} (user: {UserId})",
                instanceId, definition.Id, userId);

            _metricsCollector.IncrementCounter("agent.instances.created", tags: new Dictionary<string, string>
            {
                ["agent_id"] = definition.Id,
                ["agent_type"] = definition.Type.ToString(),
                ["user_id"] = userId
            });

            return instance;
        }
        finally
        {
            _instanceSemaphore.Release();
        }
    }

    public async Task<AgentInstance> StartInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));

        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"Instance {instanceId} not found");

        if (instance.Status == AgentInstanceStatus.Running)
            return instance;

        await _instanceSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Update status to starting
            instance = instance with { Status = AgentInstanceStatus.Starting, UpdatedAt = DateTimeOffset.UtcNow };
            _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

            OnInstanceStatusChanged(instance, AgentInstanceStatus.Stopped);

            try
            {
                // Start the agent process
                await StartAgentProcessAsync(instance, cancellationToken);

                // Update status to running
                instance = instance with
                {
                    Status = AgentInstanceStatus.Running,
                    StartedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

                // Initialize performance counters
                InitializePerformanceCounters(instance);

                _logger.LogInformation("Started agent instance {InstanceId}", instanceId);

                _metricsCollector.IncrementCounter("agent.instances.started", tags: new Dictionary<string, string>
                {
                    ["instance_id"] = instanceId,
                    ["agent_id"] = instance.AgentId
                });

                OnInstanceStatusChanged(instance, AgentInstanceStatus.Starting);
            }
            catch (Exception ex)
            {
                // Update status to failed
                var error = new AgentError
                {
                    Code = "START_FAILED",
                    Message = "Failed to start agent instance",
                    Details = ex.Message,
                    StackTrace = ex.StackTrace,
                    Severity = ErrorSeverity.Error
                };

                instance = instance with
                {
                    Status = AgentInstanceStatus.Failed,
                    Error = error,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

                OnInstanceError(instance, error);
                throw;
            }

            return instance;
        }
        finally
        {
            _instanceSemaphore.Release();
        }
    }

    public async Task<AgentInstance> StopInstanceAsync(string instanceId, bool graceful = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));

        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"Instance {instanceId} not found");

        if (instance.Status == AgentInstanceStatus.Stopped)
            return instance;

        await _instanceSemaphore.WaitAsync(cancellationToken);
        try
        {
            var previousStatus = instance.Status;

            // Update status to stopping
            instance = instance with { Status = AgentInstanceStatus.Stopping, UpdatedAt = DateTimeOffset.UtcNow };
            _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

            OnInstanceStatusChanged(instance, previousStatus);

            try
            {
                // Stop the agent process
                await StopAgentProcessAsync(instance, graceful, cancellationToken);

                // Clean up performance counters
                CleanupPerformanceCounters(instanceId);

                // Update status to stopped
                instance = instance with
                {
                    Status = AgentInstanceStatus.Stopped,
                    StoppedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

                _logger.LogInformation("Stopped agent instance {InstanceId}", instanceId);

                _metricsCollector.IncrementCounter("agent.instances.stopped", tags: new Dictionary<string, string>
                {
                    ["instance_id"] = instanceId,
                    ["agent_id"] = instance.AgentId,
                    ["graceful"] = graceful.ToString()
                });

                OnInstanceStatusChanged(instance, AgentInstanceStatus.Stopping);
            }
            catch (Exception ex)
            {
                var error = new AgentError
                {
                    Code = "STOP_FAILED",
                    Message = "Failed to stop agent instance",
                    Details = ex.Message,
                    StackTrace = ex.StackTrace,
                    Severity = ErrorSeverity.Warning
                };

                instance = instance with
                {
                    Status = AgentInstanceStatus.Failed,
                    Error = error,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

                OnInstanceError(instance, error);
                throw;
            }

            return instance;
        }
        finally
        {
            _instanceSemaphore.Release();
        }
    }

    public async Task<AgentInstance> PauseInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));

        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"Instance {instanceId} not found");

        if (instance.Status != AgentInstanceStatus.Running)
            throw new InvalidOperationException($"Cannot pause instance {instanceId} in state {instance.Status}");

        if (_processes.TryGetValue(instanceId, out var process) && !process.HasExited)
        {
            // Suspend the process (Windows-specific)
            SuspendProcess(process.Id);

            instance = instance with { Status = AgentInstanceStatus.Paused, UpdatedAt = DateTimeOffset.UtcNow };
            _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

            OnInstanceStatusChanged(instance, AgentInstanceStatus.Running);

            _logger.LogInformation("Paused agent instance {InstanceId}", instanceId);

            _metricsCollector.IncrementCounter("agent.instances.paused", tags: new Dictionary<string, string>
            {
                ["instance_id"] = instanceId,
                ["agent_id"] = instance.AgentId
            });
        }

        return instance;
    }

    public async Task<AgentInstance> ResumeInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));

        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"Instance {instanceId} not found");

        if (instance.Status != AgentInstanceStatus.Paused)
            throw new InvalidOperationException($"Cannot resume instance {instanceId} in state {instance.Status}");

        if (_processes.TryGetValue(instanceId, out var process) && !process.HasExited)
        {
            // Resume the process (Windows-specific)
            ResumeProcess(process.Id);

            instance = instance with { Status = AgentInstanceStatus.Running, UpdatedAt = DateTimeOffset.UtcNow };
            _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

            OnInstanceStatusChanged(instance, AgentInstanceStatus.Paused);

            _logger.LogInformation("Resumed agent instance {InstanceId}", instanceId);

            _metricsCollector.IncrementCounter("agent.instances.resumed", tags: new Dictionary<string, string>
            {
                ["instance_id"] = instanceId,
                ["agent_id"] = instance.AgentId
            });
        }

        return instance;
    }

    public async Task<AgentInstance> RestartInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        // Stop the instance first
        await StopInstanceAsync(instanceId, true, cancellationToken);

        // Wait a moment for cleanup
        await Task.Delay(1000, cancellationToken);

        // Start it again
        return await StartInstanceAsync(instanceId, cancellationToken);
    }

    public async Task DestroyInstanceAsync(string instanceId, bool force = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));

        if (!_instances.TryGetValue(instanceId, out var instance))
            return; // Already destroyed

        try
        {
            // Stop the instance if running
            if (instance.Status != AgentInstanceStatus.Stopped)
            {
                await StopInstanceAsync(instanceId, !force, cancellationToken);
            }
        }
        catch (Exception ex) when (force)
        {
            _logger.LogWarning(ex, "Force destroying instance {InstanceId} after stop failed", instanceId);
        }

        // Clean up resources
        _instances.TryRemove(instanceId, out _);
        _processes.TryRemove(instanceId, out var process);
        process?.Dispose();
        CleanupPerformanceCounters(instanceId);

        // Clean up working directory
        try
        {
            if (!string.IsNullOrEmpty(instance.WorkingDirectory) && Directory.Exists(instance.WorkingDirectory))
            {
                Directory.Delete(instance.WorkingDirectory, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up working directory for instance {InstanceId}", instanceId);
        }

        _logger.LogInformation("Destroyed agent instance {InstanceId}", instanceId);

        _metricsCollector.IncrementCounter("agent.instances.destroyed", tags: new Dictionary<string, string>
        {
            ["instance_id"] = instanceId,
            ["agent_id"] = instance.AgentId,
            ["force"] = force.ToString()
        });
    }

    public Task<AgentInstance?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));

        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
    }

    public async Task<IEnumerable<AgentInstance>> ListInstancesAsync(
        string? userId = null,
        AgentInstanceStatus? status = null,
        string? agentId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= await _currentUserService.GetCurrentUserIdAsync(cancellationToken);

        var instances = _instances.Values.Where(i => i.UserId == userId);

        if (status.HasValue)
            instances = instances.Where(i => i.Status == status.Value);

        if (!string.IsNullOrEmpty(agentId))
            instances = instances.Where(i => i.AgentId == agentId);

        return instances.OrderBy(i => i.CreatedAt);
    }

    public async Task<AgentInstance> UpdateInstanceConfigurationAsync(
        string instanceId,
        Dictionary<string, object> configuration,
        bool restart = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(instanceId)) throw new ArgumentNullException(nameof(instanceId));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"Instance {instanceId} not found");

        // Update configuration
        var newConfig = new Dictionary<string, object>(instance.Configuration);
        foreach (var kvp in configuration)
        {
            newConfig[kvp.Key] = kvp.Value;
        }

        instance = instance with
        {
            Configuration = newConfig,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _instances.TryUpdate(instanceId, instance, _instances[instanceId]);

        if (restart && instance.Status == AgentInstanceStatus.Running)
        {
            instance = await RestartInstanceAsync(instanceId, cancellationToken);
        }

        return instance;
    }

    public async Task<AgentResourceUsage?> GetInstanceResourceUsageAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return null;

        if (!_processes.TryGetValue(instanceId, out var process) || process.HasExited)
            return null;

        return await CollectResourceUsageAsync(process);
    }

    public Task<AgentMetrics?> GetInstanceMetricsAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return Task.FromResult<AgentMetrics?>(null);

        return Task.FromResult(instance.Metrics);
    }

    public async Task<HealthStatus> CheckInstanceHealthAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (!_instances.TryGetValue(instanceId, out var instance))
            return HealthStatus.Unknown;

        if (!_processes.TryGetValue(instanceId, out var process))
            return HealthStatus.Unhealthy;

        if (process.HasExited)
            return HealthStatus.Unhealthy;

        try
        {
            // Basic health check - process is running and responsive
            if (process.Responding)
                return HealthStatus.Healthy;
            else
                return HealthStatus.Degraded;
        }
        catch
        {
            return HealthStatus.Unhealthy;
        }
    }

    public async Task<IEnumerable<LogEntry>> GetInstanceLogsAsync(
        string instanceId,
        DateTimeOffset? since = null,
        int? tail = null,
        bool follow = false,
        CancellationToken cancellationToken = default)
    {
        // This is a placeholder implementation
        // In a real implementation, you would read from log files or a logging system
        var logs = new List<LogEntry>();

        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var logFile = Path.Combine(instance.WorkingDirectory ?? "", "agent.log");
            if (File.Exists(logFile))
            {
                var lines = await File.ReadAllLinesAsync(logFile, cancellationToken);
                logs.AddRange(lines.Select(line => new LogEntry
                {
                    Message = line,
                    Level = Models.LogLevel.Information,
                    Timestamp = DateTimeOffset.UtcNow
                }));
            }
        }

        if (tail.HasValue)
            logs = logs.TakeLast(tail.Value).ToList();

        return logs;
    }

    private async Task StartAgentProcessAsync(AgentInstance instance, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{GetAgentExecutablePath(instance.AgentId)}\"",
            WorkingDirectory = instance.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        // Add environment variables
        foreach (var env in instance.Environment)
        {
            startInfo.EnvironmentVariables[env.Key] = env.Value;
        }

        var process = new Process { StartInfo = startInfo };

        // Set up event handlers for output
        process.OutputDataReceived += (sender, args) => LogProcessOutput(instance.InstanceId, "stdout", args.Data);
        process.ErrorDataReceived += (sender, args) => LogProcessOutput(instance.InstanceId, "stderr", args.Data);
        process.Exited += (sender, args) => OnProcessExited(instance.InstanceId);

        process.EnableRaisingEvents = true;

        if (!process.Start())
            throw new InvalidOperationException("Failed to start agent process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Update instance with process ID
        var updatedInstance = instance with { ProcessId = process.Id };
        _instances.TryUpdate(instance.InstanceId, updatedInstance, instance);

        _processes.TryAdd(instance.InstanceId, process);

        _logger.LogDebug("Started agent process {ProcessId} for instance {InstanceId}", process.Id, instance.InstanceId);
    }

    private async Task StopAgentProcessAsync(AgentInstance instance, bool graceful, CancellationToken cancellationToken)
    {
        if (!_processes.TryRemove(instance.InstanceId, out var process))
            return;

        try
        {
            if (!process.HasExited)
            {
                if (graceful)
                {
                    // Try graceful shutdown first
                    process.StandardInput.WriteLine("shutdown");

                    // Wait for graceful shutdown
                    if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
                    {
                        _logger.LogWarning("Agent process {ProcessId} did not respond to graceful shutdown, killing", process.Id);
                        process.Kill(true);
                    }
                }
                else
                {
                    process.Kill(true);
                }
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private Dictionary<string, string> CreateEnvironmentVariables(AgentDefinition definition, string instanceId)
    {
        return new Dictionary<string, string>
        {
            ["AGENT_ID"] = definition.Id,
            ["AGENT_NAME"] = definition.Name,
            ["AGENT_VERSION"] = definition.Version,
            ["INSTANCE_ID"] = instanceId,
            ["RUNTIME_MODE"] = "client"
        };
    }

    private string GetAgentExecutablePath(string agentId)
    {
        // This would typically resolve the agent's executable path
        return Path.Combine(_configuration.AgentInstallPath, agentId, "agent.dll");
    }

    private void InitializePerformanceCounters(AgentInstance instance)
    {
        if (instance.ProcessId == null) return;

        try
        {
            var counters = new[]
            {
                new PerformanceCounter("Process", "% Processor Time", $"dotnet#{instance.ProcessId}"),
                new PerformanceCounter("Process", "Working Set", $"dotnet#{instance.ProcessId}"),
                new PerformanceCounter("Process", "Private Bytes", $"dotnet#{instance.ProcessId}"),
                new PerformanceCounter("Process", "Thread Count", $"dotnet#{instance.ProcessId}"),
                new PerformanceCounter("Process", "Handle Count", $"dotnet#{instance.ProcessId}")
            };

            _performanceCounters.TryAdd(instance.InstanceId, counters);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize performance counters for instance {InstanceId}", instance.InstanceId);
        }
    }

    private void CleanupPerformanceCounters(string instanceId)
    {
        if (_performanceCounters.TryRemove(instanceId, out var counters))
        {
            foreach (var counter in counters)
            {
                try
                {
                    counter.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose performance counter");
                }
            }
        }
    }

    private async Task<AgentResourceUsage> CollectResourceUsageAsync(Process process)
    {
        try
        {
            process.Refresh();

            return new AgentResourceUsage
            {
                CpuUsagePercent = GetCpuUsage(process),
                MemoryUsageMb = process.WorkingSet64 / 1024 / 1024,
                ThreadCount = process.Threads.Count,
                FileHandles = process.HandleCount,
                MeasuredAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect resource usage for process {ProcessId}", process.Id);
            return new AgentResourceUsage
            {
                CpuUsagePercent = 0,
                MemoryUsageMb = 0,
                ThreadCount = 0,
                FileHandles = 0,
                MeasuredAt = DateTimeOffset.UtcNow
            };
        }
    }

    private double GetCpuUsage(Process process)
    {
        // This is a simplified CPU usage calculation
        // In production, you'd want a more accurate implementation
        try
        {
            return process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount / 1000.0;
        }
        catch
        {
            return 0.0;
        }
    }

    private void SuspendProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            foreach (ProcessThread thread in process.Threads)
            {
                var threadHandle = OpenThread(0x0002, false, thread.Id);
                if (threadHandle != IntPtr.Zero)
                {
                    SuspendThread(threadHandle);
                    CloseHandle(threadHandle);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to suspend process {ProcessId}", processId);
        }
    }

    private void ResumeProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            foreach (ProcessThread thread in process.Threads)
            {
                var threadHandle = OpenThread(0x0002, false, thread.Id);
                if (threadHandle != IntPtr.Zero)
                {
                    ResumeThread(threadHandle);
                    CloseHandle(threadHandle);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resume process {ProcessId}", processId);
        }
    }

    private void MonitorInstances(object? state)
    {
        foreach (var kvp in _instances)
        {
            var instanceId = kvp.Key;
            var instance = kvp.Value;

            if (instance.Status == AgentInstanceStatus.Running && _processes.TryGetValue(instanceId, out var process))
            {
                try
                {
                    if (process.HasExited)
                    {
                        // Process has exited unexpectedly
                        var error = new AgentError
                        {
                            Code = "PROCESS_EXITED",
                            Message = "Agent process exited unexpectedly",
                            Details = $"Exit code: {process.ExitCode}",
                            Severity = ErrorSeverity.Error
                        };

                        var updatedInstance = instance with
                        {
                            Status = AgentInstanceStatus.Failed,
                            Error = error,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };

                        _instances.TryUpdate(instanceId, updatedInstance, instance);
                        OnInstanceError(updatedInstance, error);

                        _processes.TryRemove(instanceId, out _);
                        CleanupPerformanceCounters(instanceId);
                    }
                    else
                    {
                        // Collect resource usage
                        var resourceUsage = CollectResourceUsageAsync(process).GetAwaiter().GetResult();
                        var metricsUpdated = instance with
                        {
                            ResourceUsage = resourceUsage,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };

                        _instances.TryUpdate(instanceId, metricsUpdated, instance);
                        OnInstanceMetricsUpdated(metricsUpdated);

                        // Record metrics
                        _metricsCollector.RecordGauge("agent.instance.cpu_usage", resourceUsage.CpuUsagePercent,
                            new Dictionary<string, string> { ["instance_id"] = instanceId });
                        _metricsCollector.RecordGauge("agent.instance.memory_usage_mb", resourceUsage.MemoryUsageMb,
                            new Dictionary<string, string> { ["instance_id"] = instanceId });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error monitoring instance {InstanceId}", instanceId);
                }
            }
        }
    }

    private void LogProcessOutput(string instanceId, string stream, string? data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            _logger.LogDebug("Agent {InstanceId} {Stream}: {Data}", instanceId, stream, data);
        }
    }

    private void OnProcessExited(string instanceId)
    {
        _logger.LogInformation("Agent process for instance {InstanceId} exited", instanceId);

        if (_instances.TryGetValue(instanceId, out var instance))
        {
            // Update instance status if it wasn't already updated
            if (instance.Status == AgentInstanceStatus.Running)
            {
                var updatedInstance = instance with
                {
                    Status = AgentInstanceStatus.Stopped,
                    StoppedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                _instances.TryUpdate(instanceId, updatedInstance, instance);
                OnInstanceStatusChanged(updatedInstance, AgentInstanceStatus.Running);
            }
        }
    }

    private void OnInstanceStatusChanged(AgentInstance instance, AgentInstanceStatus previousStatus)
    {
        InstanceStatusChanged?.Invoke(this, new AgentInstanceEventArgs
        {
            Instance = instance,
            PreviousStatus = previousStatus
        });
    }

    private void OnInstanceMetricsUpdated(AgentInstance instance)
    {
        InstanceMetricsUpdated?.Invoke(this, new AgentInstanceEventArgs
        {
            Instance = instance
        });
    }

    private void OnInstanceError(AgentInstance instance, AgentError error)
    {
        InstanceError?.Invoke(this, new AgentInstanceErrorEventArgs
        {
            Instance = instance,
            Error = error
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _monitoringTimer?.Dispose();
        _instanceSemaphore?.Dispose();

        // Stop all running instances
        var runningInstances = _instances.Values.Where(i => i.Status == AgentInstanceStatus.Running).ToList();
        foreach (var instance in runningInstances)
        {
            try
            {
                StopInstanceAsync(instance.InstanceId, false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop instance {InstanceId} during dispose", instance.InstanceId);
            }
        }

        // Dispose all processes
        foreach (var process in _processes.Values)
        {
            try
            {
                process?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose process during cleanup");
            }
        }

        // Cleanup performance counters
        foreach (var instanceId in _performanceCounters.Keys)
        {
            CleanupPerformanceCounters(instanceId);
        }

        _disposed = true;
    }

    // Windows API imports for process suspension/resumption
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, int dwThreadId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SuspendThread(IntPtr hThread);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint ResumeThread(IntPtr hThread);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
}

/// <summary>
/// Agent client configuration
/// </summary>
public class AgentClientConfiguration
{
    /// <summary>
    /// Path where agent installations are stored
    /// </summary>
    public string AgentInstallPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Hartonomous", "Agents");

    /// <summary>
    /// Path for agent workspace directories
    /// </summary>
    public string AgentWorkspacePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hartonomous", "Workspaces");

    /// <summary>
    /// Maximum number of concurrent agent instances per user
    /// </summary>
    public int MaxInstancesPerUser { get; set; } = 10;

    /// <summary>
    /// Default timeout for agent operations in seconds
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Monitoring interval in seconds
    /// </summary>
    public int MonitoringIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to enable resource monitoring
    /// </summary>
    public bool EnableResourceMonitoring { get; set; } = true;

    /// <summary>
    /// Whether to enable process sandboxing
    /// </summary>
    public bool EnableSandboxing { get; set; } = true;

    /// <summary>
    /// Security validation policy configuration
    /// </summary>
    public SecurityValidationPolicy Security { get; set; } = new();
}
