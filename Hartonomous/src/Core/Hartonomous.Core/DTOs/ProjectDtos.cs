using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Core.DTOs;

public record ProjectDto(Guid ProjectId, string ProjectName, DateTime CreatedAt);

public record CreateProjectRequest(
    [Required(ErrorMessage = "Project name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Project name must be between 1 and 256 characters")]
    string ProjectName);