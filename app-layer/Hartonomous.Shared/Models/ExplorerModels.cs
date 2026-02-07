using System.Text.Json.Serialization;

namespace Hartonomous.Shared.Models;

public class ExplorerNode
{
    public string Id { get; set; } = string.Empty; // Usually the text itself or a hash
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Concept"; // Domain, Concept, Entity
    public double Score { get; set; }
    public bool HasChildren { get; set; } = true;
}

public class ExplorerDetails
{
    public string Text { get; set; } = string.Empty;
    public double[] S3Position { get; set; } = new double[4];
    public ulong HilbertIndexHi { get; set; }
    public ulong HilbertIndexLo { get; set; }
    public double Elo { get; set; }
}
