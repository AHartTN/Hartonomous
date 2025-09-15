namespace Hartonomous.Core.DTOs;

public record QueryRequest(string SemanticQuery, Guid ProjectId);
public record ComponentDto(Guid ComponentId, string ComponentName, string ComponentType);
public record QueryResponse(List<ComponentDto> Components);