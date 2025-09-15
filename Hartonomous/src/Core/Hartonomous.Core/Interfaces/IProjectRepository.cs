using Hartonomous.Core.DTOs;

namespace Hartonomous.Core.Interfaces;

public interface IProjectRepository
{
    Task<IEnumerable<ProjectDto>> GetProjectsByUserAsync(string userId);
    Task<ProjectDto?> GetProjectByIdAsync(Guid projectId, string userId);
    Task<Guid> CreateProjectAsync(CreateProjectRequest request, string userId);
    Task<bool> DeleteProjectAsync(Guid projectId, string userId);
}