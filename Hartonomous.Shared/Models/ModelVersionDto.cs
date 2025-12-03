namespace Hartonomous.Shared.Models;

public class ModelVersionDto
{
    public long Id { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long RootAtomId { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Active";
}

public class CreateModelVersionRequest
{
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long RootAtomId { get; set; }
    public string Architecture { get; set; } = string.Empty;
    public long ParameterCount { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
