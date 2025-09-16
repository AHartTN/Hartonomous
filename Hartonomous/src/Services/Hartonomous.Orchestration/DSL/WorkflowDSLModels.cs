namespace Hartonomous.Orchestration.DSL;

/// <summary>
/// Workflow graph representation
/// </summary>
public class WorkflowGraph
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public Dictionary<string, object> Parameters { get; set; } = new();
    public Dictionary<string, WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
    public WorkflowTrigger? Trigger { get; set; }
    public WorkflowTimeout? Timeout { get; set; }
    public WorkflowRetry? Retry { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Workflow node definition
/// </summary>
public class WorkflowNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
    public Dictionary<string, object> Input { get; set; } = new();
    public Dictionary<string, object> Output { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public WorkflowCondition? Condition { get; set; }
    public WorkflowTimeout? Timeout { get; set; }
    public WorkflowRetry? Retry { get; set; }
    public WorkflowErrorHandling? ErrorHandling { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int? MaxParallelism { get; set; }
    public int Priority { get; set; } = 0;
}

/// <summary>
/// Workflow edge (connection between nodes)
/// </summary>
public class WorkflowEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public WorkflowCondition? Condition { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Workflow trigger definition
/// </summary>
public class WorkflowTrigger
{
    public string Type { get; set; } = string.Empty; // manual, schedule, event, webhook
    public Dictionary<string, object> Configuration { get; set; } = new();
}

/// <summary>
/// Workflow condition for conditional execution
/// </summary>
public class WorkflowCondition
{
    public string Expression { get; set; } = string.Empty;
    public string Type { get; set; } = "javascript"; // javascript, xpath, jsonpath
    public Dictionary<string, object> Variables { get; set; } = new();
}

/// <summary>
/// Workflow timeout configuration
/// </summary>
public class WorkflowTimeout
{
    public TimeSpan Duration { get; set; }
    public string OnTimeoutAction { get; set; } = "fail"; // fail, continue, retry
    public Dictionary<string, object> Configuration { get; set; } = new();
}

/// <summary>
/// Workflow retry configuration
/// </summary>
public class WorkflowRetry
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
    public double BackoffMultiplier { get; set; } = 2.0;
    public List<string> RetryOnErrors { get; set; } = new();
    public List<string> DoNotRetryOnErrors { get; set; } = new();
}

/// <summary>
/// Workflow error handling configuration
/// </summary>
public class WorkflowErrorHandling
{
    public string OnError { get; set; } = "fail"; // fail, continue, retry, compensate
    public string? CompensationNode { get; set; }
    public string? FallbackNode { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = new();
}

/// <summary>
/// Workflow validation result from DSL parsing
/// </summary>
public class WorkflowValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationWarning> Warnings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Validation error
/// </summary>
public class ValidationError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? NodeId { get; set; }
    public string? Path { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

/// <summary>
/// Validation warning
/// </summary>
public class ValidationWarning
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? NodeId { get; set; }
    public string? Path { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

/// <summary>
/// Supported node types in the workflow DSL
/// </summary>
public static class WorkflowNodeTypes
{
    public const string Action = "action";
    public const string Condition = "condition";
    public const string Loop = "loop";
    public const string Parallel = "parallel";
    public const string Wait = "wait";
    public const string Script = "script";
    public const string HttpRequest = "http";
    public const string DatabaseQuery = "database";
    public const string EmailSend = "email";
    public const string FileOperation = "file";
    public const string AgentCall = "agent";
    public const string Subprocess = "subprocess";
    public const string DataTransform = "transform";
    public const string Notification = "notification";
    public const string Approval = "approval";
    public const string Start = "start";
    public const string End = "end";
}

/// <summary>
/// Supported trigger types
/// </summary>
public static class WorkflowTriggerTypes
{
    public const string Manual = "manual";
    public const string Schedule = "schedule";
    public const string Event = "event";
    public const string Webhook = "webhook";
    public const string FileWatch = "filewatch";
    public const string DatabaseChange = "dbchange";
    public const string MessageQueue = "queue";
}

/// <summary>
/// Supported condition types
/// </summary>
public static class WorkflowConditionTypes
{
    public const string JavaScript = "javascript";
    public const string JsonPath = "jsonpath";
    public const string XPath = "xpath";
    public const string Regex = "regex";
    public const string Simple = "simple";
}