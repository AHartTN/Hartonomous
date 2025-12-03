namespace Hartonomous.Shared.Models;

public class IngestionJobDto
{
    public long Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string JobStatus { get; set; } = "Pending";
    public long CurrentAtomOffset { get; set; }
    public long TotalAtoms { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public double ProgressPercentage => TotalAtoms > 0 ? (double)CurrentAtomOffset / TotalAtoms * 100 : 0;
}

public class CreateIngestionJobRequest
{
    public string JobType { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}

public class IngestionJobStatusUpdate
{
    public long JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long CurrentOffset { get; set; }
    public string? ErrorMessage { get; set; }
}
