using Hartonomous.Orchestration.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Hartonomous.Orchestration.DSL;

/// <summary>
/// Workflow DSL parser implementation
/// </summary>
public class WorkflowDSLParser : IWorkflowDSLParser
{
    private readonly ILogger<WorkflowDSLParser> _logger;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ISerializer _yamlSerializer;

    public WorkflowDSLParser(ILogger<WorkflowDSLParser> logger)
    {
        _logger = logger;

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public async Task<WorkflowGraph> ParseWorkflowAsync(string dslContent)
    {
        try
        {
            _logger.LogDebug("Parsing workflow DSL content");

            // Try to determine format (JSON vs YAML)
            dslContent = dslContent.Trim();

            if (dslContent.StartsWith("{") || dslContent.StartsWith("["))
            {
                return await ParseWorkflowFromJsonAsync(dslContent);
            }
            else
            {
                return await ParseWorkflowFromYamlAsync(dslContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse workflow DSL");
            throw new ArgumentException($"Invalid workflow DSL: {ex.Message}", ex);
        }
    }

    public async Task<WorkflowGraph> ParseWorkflowFromYamlAsync(string yamlContent)
    {
        try
        {
            _logger.LogDebug("Parsing workflow from YAML");

            var workflowData = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);
            return await Task.FromResult(ParseWorkflowData(workflowData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse YAML workflow");
            throw new ArgumentException($"Invalid YAML workflow: {ex.Message}", ex);
        }
    }

    public async Task<WorkflowGraph> ParseWorkflowFromJsonAsync(string jsonContent)
    {
        try
        {
            _logger.LogDebug("Parsing workflow from JSON");

            var workflowData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
            if (workflowData == null)
            {
                throw new ArgumentException("Invalid JSON content");
            }

            return await Task.FromResult(ParseWorkflowData(workflowData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse JSON workflow");
            throw new ArgumentException($"Invalid JSON workflow: {ex.Message}", ex);
        }
    }

    public async Task<WorkflowValidationResult> ValidateDSLAsync(string dslContent)
    {
        var result = new WorkflowValidationResult
        {
            IsValid = true,
            Errors = new List<ValidationError>(),
            Warnings = new List<ValidationWarning>()
        };

        try
        {
            var workflow = await ParseWorkflowAsync(dslContent);

            // Validate workflow structure
            if (string.IsNullOrEmpty(workflow.Name))
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "MISSING_NAME",
                    Message = "Workflow name is required"
                });
            }

            if (!workflow.Nodes.Any())
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "NO_NODES",
                    Message = "Workflow must contain at least one node"
                });
            }

            // Validate node dependencies
            foreach (var node in workflow.Nodes.Values)
            {
                foreach (var dependency in node.Dependencies)
                {
                    if (!workflow.Nodes.ContainsKey(dependency))
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Code = "INVALID_DEPENDENCY",
                            Message = $"Node {node.Id} depends on non-existent node {dependency}",
                            NodeId = node.Id
                        });
                    }
                }
            }

            // Validate edge references
            foreach (var edge in workflow.Edges)
            {
                if (!workflow.Nodes.ContainsKey(edge.From))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "INVALID_EDGE_FROM",
                        Message = $"Edge references non-existent source node {edge.From}"
                    });
                }

                if (!workflow.Nodes.ContainsKey(edge.To))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "INVALID_EDGE_TO",
                        Message = $"Edge references non-existent target node {edge.To}"
                    });
                }
            }

            // Check for circular dependencies
            var circularDeps = DetectCircularDependencies(workflow);
            if (circularDeps.Any())
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "CIRCULAR_DEPENDENCY",
                    Message = $"Circular dependency detected: {string.Join(" -> ", circularDeps)}"
                });
            }

            // Add warnings for potential issues
            if (!workflow.Nodes.Values.Any(n => n.Type == WorkflowNodeTypes.Start))
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "NO_START_NODE",
                    Message = "No explicit start node defined. First node will be used as start."
                });
            }

            if (!workflow.Nodes.Values.Any(n => n.Type == WorkflowNodeTypes.End))
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "NO_END_NODE",
                    Message = "No explicit end node defined."
                });
            }

            result.IsValid = !result.Errors.Any();
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError
            {
                Code = "PARSE_ERROR",
                Message = ex.Message
            });
        }

        return result;
    }

    public async Task<string> ConvertToExecutableAsync(WorkflowGraph graph)
    {
        try
        {
            _logger.LogDebug("Converting workflow graph to executable format");

            // Convert to LangGraph-compatible format
            var executable = new
            {
                name = graph.Name,
                description = graph.Description,
                version = graph.Version,
                nodes = graph.Nodes.Values.Select(n => new
                {
                    id = n.Id,
                    name = n.Name,
                    type = n.Type,
                    configuration = n.Configuration,
                    input = n.Input,
                    dependencies = n.Dependencies,
                    condition = n.Condition?.Expression,
                    timeout = n.Timeout?.Duration.TotalSeconds,
                    retry = n.Retry?.MaxAttempts ?? 0
                }),
                edges = graph.Edges.Select(e => new
                {
                    from = e.From,
                    to = e.To,
                    condition = e.Condition?.Expression
                }),
                parameters = graph.Parameters,
                metadata = graph.Metadata
            };

            return await Task.FromResult(JsonSerializer.Serialize(executable, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert workflow to executable format");
            throw;
        }
    }

    public async Task<string> GenerateDSLAsync(WorkflowGraph graph)
    {
        try
        {
            _logger.LogDebug("Generating DSL from workflow graph");

            var dslObject = new
            {
                name = graph.Name,
                description = graph.Description,
                version = graph.Version,
                parameters = graph.Parameters,
                nodes = graph.Nodes.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        name = kvp.Value.Name,
                        type = kvp.Value.Type,
                        description = kvp.Value.Description,
                        configuration = kvp.Value.Configuration,
                        input = kvp.Value.Input,
                        dependencies = kvp.Value.Dependencies,
                        condition = kvp.Value.Condition?.Expression,
                        timeout = kvp.Value.Timeout?.Duration.TotalSeconds,
                        retry = kvp.Value.Retry
                    }
                ),
                edges = graph.Edges.Select(e => new
                {
                    from = e.From,
                    to = e.To,
                    condition = e.Condition?.Expression
                }),
                metadata = graph.Metadata
            };

            return await Task.FromResult(_yamlSerializer.Serialize(dslObject));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate DSL from workflow graph");
            throw;
        }
    }

    public string GetSupportedVersion()
    {
        return "1.0";
    }

    public async Task<WorkflowValidationResult> ValidateDependenciesAsync(WorkflowGraph graph)
    {
        var result = new WorkflowValidationResult
        {
            IsValid = true,
            Errors = new List<ValidationError>(),
            Warnings = new List<ValidationWarning>()
        };

        // Check for orphaned nodes (no incoming or outgoing edges)
        var nodesWithIncoming = new HashSet<string>(graph.Edges.Select(e => e.To));
        var nodesWithOutgoing = new HashSet<string>(graph.Edges.Select(e => e.From));

        foreach (var node in graph.Nodes.Values)
        {
            if (!nodesWithIncoming.Contains(node.Id) && !nodesWithOutgoing.Contains(node.Id) &&
                node.Type != WorkflowNodeTypes.Start && node.Type != WorkflowNodeTypes.End)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "ORPHANED_NODE",
                    Message = $"Node {node.Id} has no connections",
                    NodeId = node.Id
                });
            }
        }

        // Check for unreachable nodes
        var reachableNodes = await GetReachableNodesAsync(graph);
        foreach (var node in graph.Nodes.Values)
        {
            if (!reachableNodes.Contains(node.Id))
            {
                result.Warnings.Add(new ValidationWarning
                {
                    Code = "UNREACHABLE_NODE",
                    Message = $"Node {node.Id} is not reachable from start node",
                    NodeId = node.Id
                });
            }
        }

        return result;
    }

    public async Task<WorkflowGraph> OptimizeGraphAsync(WorkflowGraph graph)
    {
        try
        {
            _logger.LogDebug("Optimizing workflow graph");

            var optimizedGraph = new WorkflowGraph
            {
                Name = graph.Name,
                Description = graph.Description,
                Version = graph.Version,
                Parameters = new Dictionary<string, object>(graph.Parameters),
                Nodes = new Dictionary<string, WorkflowNode>(),
                Edges = new List<WorkflowEdge>(),
                Trigger = graph.Trigger,
                Timeout = graph.Timeout,
                Retry = graph.Retry,
                Metadata = new Dictionary<string, object>(graph.Metadata)
            };

            // Remove unreachable nodes
            var reachableNodes = await GetReachableNodesAsync(graph);
            foreach (var kvp in graph.Nodes)
            {
                if (reachableNodes.Contains(kvp.Key))
                {
                    optimizedGraph.Nodes[kvp.Key] = kvp.Value;
                }
            }

            // Remove edges to unreachable nodes
            foreach (var edge in graph.Edges)
            {
                if (reachableNodes.Contains(edge.From) && reachableNodes.Contains(edge.To))
                {
                    optimizedGraph.Edges.Add(edge);
                }
            }

            // Merge redundant nodes (nodes with same configuration and single incoming/outgoing edge)
            // This is a placeholder for more advanced optimization logic

            return optimizedGraph;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize workflow graph");
            throw;
        }
    }

    private WorkflowGraph ParseWorkflowData(Dictionary<string, object> workflowData)
    {
        var graph = new WorkflowGraph();

        // Parse basic properties
        if (workflowData.TryGetValue("name", out var name))
            graph.Name = name.ToString() ?? string.Empty;

        if (workflowData.TryGetValue("description", out var description))
            graph.Description = description.ToString() ?? string.Empty;

        if (workflowData.TryGetValue("version", out var version))
            graph.Version = version.ToString() ?? "1.0";

        // Parse parameters
        if (workflowData.TryGetValue("parameters", out var parameters) && parameters is Dictionary<string, object> paramDict)
        {
            graph.Parameters = paramDict;
        }

        // Parse nodes
        if (workflowData.TryGetValue("nodes", out var nodes))
        {
            if (nodes is Dictionary<string, object> nodeDict)
            {
                foreach (var kvp in nodeDict)
                {
                    if (kvp.Value is Dictionary<string, object> nodeData)
                    {
                        var node = ParseNode(kvp.Key, nodeData);
                        graph.Nodes[kvp.Key] = node;
                    }
                }
            }
        }

        // Parse edges
        if (workflowData.TryGetValue("edges", out var edges))
        {
            if (edges is List<object> edgeList)
            {
                foreach (var edgeObj in edgeList)
                {
                    if (edgeObj is Dictionary<string, object> edgeData)
                    {
                        var edge = ParseEdge(edgeData);
                        graph.Edges.Add(edge);
                    }
                }
            }
        }

        // Parse trigger
        if (workflowData.TryGetValue("trigger", out var trigger) && trigger is Dictionary<string, object> triggerData)
        {
            graph.Trigger = ParseTrigger(triggerData);
        }

        // Parse metadata
        if (workflowData.TryGetValue("metadata", out var metadata) && metadata is Dictionary<string, object> metaDict)
        {
            graph.Metadata = metaDict;
        }

        return graph;
    }

    private WorkflowNode ParseNode(string nodeId, Dictionary<string, object> nodeData)
    {
        var node = new WorkflowNode
        {
            Id = nodeId,
            Name = nodeData.GetValueOrDefault("name", nodeId).ToString() ?? nodeId,
            Type = nodeData.GetValueOrDefault("type", "action").ToString() ?? "action"
        };

        if (nodeData.TryGetValue("description", out var description))
            node.Description = description.ToString();

        if (nodeData.TryGetValue("configuration", out var config) && config is Dictionary<string, object> configDict)
            node.Configuration = configDict;

        if (nodeData.TryGetValue("input", out var input) && input is Dictionary<string, object> inputDict)
            node.Input = inputDict;

        if (nodeData.TryGetValue("dependencies", out var deps) && deps is List<object> depList)
            node.Dependencies = depList.Select(d => d.ToString() ?? string.Empty).ToList();

        if (nodeData.TryGetValue("condition", out var condition))
        {
            node.Condition = new WorkflowCondition
            {
                Expression = condition.ToString() ?? string.Empty
            };
        }

        if (nodeData.TryGetValue("timeout", out var timeout) && double.TryParse(timeout.ToString(), out var timeoutSeconds))
        {
            node.Timeout = new WorkflowTimeout
            {
                Duration = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        if (nodeData.TryGetValue("retry", out var retry) && retry is Dictionary<string, object> retryData)
        {
            node.Retry = ParseRetry(retryData);
        }

        return node;
    }

    private WorkflowEdge ParseEdge(Dictionary<string, object> edgeData)
    {
        var edge = new WorkflowEdge
        {
            From = edgeData.GetValueOrDefault("from", string.Empty).ToString() ?? string.Empty,
            To = edgeData.GetValueOrDefault("to", string.Empty).ToString() ?? string.Empty
        };

        if (edgeData.TryGetValue("condition", out var condition))
        {
            edge.Condition = new WorkflowCondition
            {
                Expression = condition.ToString() ?? string.Empty
            };
        }

        return edge;
    }

    private WorkflowTrigger ParseTrigger(Dictionary<string, object> triggerData)
    {
        return new WorkflowTrigger
        {
            Type = triggerData.GetValueOrDefault("type", "manual").ToString() ?? "manual",
            Configuration = triggerData.GetValueOrDefault("configuration", new Dictionary<string, object>()) as Dictionary<string, object> ?? new Dictionary<string, object>()
        };
    }

    private WorkflowRetry ParseRetry(Dictionary<string, object> retryData)
    {
        var retry = new WorkflowRetry();

        if (retryData.TryGetValue("maxAttempts", out var maxAttempts) && int.TryParse(maxAttempts.ToString(), out var attempts))
            retry.MaxAttempts = attempts;

        if (retryData.TryGetValue("initialDelay", out var initialDelay) && double.TryParse(initialDelay.ToString(), out var delay))
            retry.InitialDelay = TimeSpan.FromSeconds(delay);

        return retry;
    }

    private List<string> DetectCircularDependencies(WorkflowGraph graph)
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var node in graph.Nodes.Keys)
        {
            if (!visited.Contains(node))
            {
                var cycle = DetectCycleRecursive(graph, node, visited, recursionStack, path);
                if (cycle.Any())
                {
                    return cycle;
                }
            }
        }

        return new List<string>();
    }

    private List<string> DetectCycleRecursive(WorkflowGraph graph, string nodeId,
        HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
    {
        visited.Add(nodeId);
        recursionStack.Add(nodeId);
        path.Add(nodeId);

        if (graph.Nodes.TryGetValue(nodeId, out var node))
        {
            foreach (var dependency in node.Dependencies)
            {
                if (!visited.Contains(dependency))
                {
                    var cycle = DetectCycleRecursive(graph, dependency, visited, recursionStack, path);
                    if (cycle.Any())
                    {
                        return cycle;
                    }
                }
                else if (recursionStack.Contains(dependency))
                {
                    // Found cycle
                    var cycleStart = path.IndexOf(dependency);
                    return path.Skip(cycleStart).ToList();
                }
            }
        }

        recursionStack.Remove(nodeId);
        path.RemoveAt(path.Count - 1);
        return new List<string>();
    }

    private async Task<HashSet<string>> GetReachableNodesAsync(WorkflowGraph graph)
    {
        var reachable = new HashSet<string>();
        var startNodes = graph.Nodes.Values
            .Where(n => n.Type == WorkflowNodeTypes.Start || !n.Dependencies.Any())
            .Select(n => n.Id)
            .ToList();

        if (!startNodes.Any())
        {
            startNodes.Add(graph.Nodes.Keys.First()); // Use first node if no start node
        }

        var queue = new Queue<string>(startNodes);
        reachable.UnionWith(startNodes);

        while (queue.Any())
        {
            var current = queue.Dequeue();
            var nextNodes = graph.Edges
                .Where(e => e.From == current)
                .Select(e => e.To)
                .Where(to => !reachable.Contains(to));

            foreach (var next in nextNodes)
            {
                reachable.Add(next);
                queue.Enqueue(next);
            }
        }

        return await Task.FromResult(reachable);
    }
}