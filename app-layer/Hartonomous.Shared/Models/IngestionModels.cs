namespace Hartonomous.Shared.Models;

public class IngestTextRequest
{
    public string Text { get; set; } = string.Empty;
}

public class IngestFileRequest
{
    public string FilePath { get; set; } = string.Empty;
}

public class IngestStats
{
    public int Atoms { get; set; }
    public int Compositions { get; set; }
    public int Relations { get; set; }
    public double TimeMs { get; set; }
}