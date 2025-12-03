using Hartonomous.Shared.Models;

namespace Hartonomous.Shared.Interfaces;

public interface IIngestionService
{
    Task<IngestionJobDto> CreateJobAsync(CreateIngestionJobRequest request);
    Task<IngestionJobDto?> GetJobByIdAsync(long id);
    Task<List<IngestionJobDto>> GetActiveJobsAsync();
    Task UpdateJobStatusAsync(IngestionJobStatusUpdate update);
    Task<bool> CancelJobAsync(long id);
}
