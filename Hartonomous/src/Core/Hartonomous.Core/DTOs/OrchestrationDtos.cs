namespace Hartonomous.Core.DTOs;

public record OrchestrationRequest(string Goal, Guid ProjectId);
public record OrchestrationResponse(string Status, Guid CorrelationId, List<string> Steps);