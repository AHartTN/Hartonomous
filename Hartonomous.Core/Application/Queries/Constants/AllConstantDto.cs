namespace Hartonomous.Core.Application.Queries.Constants;

public sealed class AllConstantDto
{
    public Guid Id { get; set; }
    public string Hash { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public DateTime CreatedAt { get; set; }
}
