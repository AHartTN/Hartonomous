using Hartonomous.Db;
using Hartonomous.Db.Entities;
using Hartonomous.Shared.Interfaces;
using Hartonomous.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Hartonomous.Api.Services;

public class IngestionService : IIngestionService
{
    private readonly HartonomousDbContext _context;

    public IngestionService(HartonomousDbContext context)
    {
        _context = context;
    }

    public async Task<IngestionJobDto> CreateJobAsync(CreateIngestionJobRequest request)
    {
        var job = new IngestionJob
        {
            JobType = request.JobType,
            SourcePath = request.SourcePath,
            JobStatus = "Pending",
            CurrentAtomOffset = 0,
            TotalAtoms = 0,
            StartedAt = DateTime.UtcNow
        };

        _context.IngestionJobs.Add(job);
        await _context.SaveChangesAsync();

        return MapToDto(job);
    }

    public async Task<IngestionJobDto?> GetJobByIdAsync(long id)
    {
        var job = await _context.IngestionJobs.FindAsync(id);
        return job == null ? null : MapToDto(job);
    }

    public async Task<List<IngestionJobDto>> GetActiveJobsAsync()
    {
        var jobs = await _context.IngestionJobs
            .Where(j => j.JobStatus == "Running" || j.JobStatus == "Pending")
            .OrderByDescending(j => j.StartedAt)
            .ToListAsync();

        return jobs.Select(MapToDto).ToList();
    }

    public async Task UpdateJobStatusAsync(IngestionJobStatusUpdate update)
    {
        var job = await _context.IngestionJobs.FindAsync(update.JobId);
        if (job == null) return;

        job.JobStatus = update.Status;
        job.CurrentAtomOffset = update.CurrentOffset;
        job.ErrorMessage = update.ErrorMessage;

        if (update.Status == "Completed" || update.Status == "Failed")
        {
            job.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> CancelJobAsync(long id)
    {
        var job = await _context.IngestionJobs.FindAsync(id);
        if (job == null || job.JobStatus == "Completed" || job.JobStatus == "Failed")
        {
            return false;
        }

        job.JobStatus = "Cancelled";
        job.CompletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    private static IngestionJobDto MapToDto(IngestionJob job)
    {
        return new IngestionJobDto
        {
            Id = job.Id,
            JobType = job.JobType,
            SourcePath = job.SourcePath,
            JobStatus = job.JobStatus,
            CurrentAtomOffset = job.CurrentAtomOffset,
            TotalAtoms = job.TotalAtoms,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ErrorMessage = job.ErrorMessage
        };
    }
}
