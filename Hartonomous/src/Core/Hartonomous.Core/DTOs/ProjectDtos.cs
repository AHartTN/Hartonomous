namespace Hartonomous.Core.DTOs;

public record ProjectDto(Guid ProjectId, string ProjectName, DateTime CreatedAt);
public record CreateProjectRequest(string ProjectName);
