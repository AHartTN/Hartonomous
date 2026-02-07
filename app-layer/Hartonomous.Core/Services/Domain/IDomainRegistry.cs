using Hartonomous.Shared.Models;

namespace Hartonomous.Core.Services.Domain;

public interface IDomainRegistry
{
    IEnumerable<ExplorerNode> GetDomains();
}

public class StaticDomainRegistry : IDomainRegistry
{
    public IEnumerable<ExplorerNode> GetDomains()
    {
        // In a real enterprise scenario, this could be loaded from a JSON config 
        // or a database table 'ontology_roots'.
        // This is still static but separated from the controller, satisfying SRP.
        return new List<ExplorerNode>
        {
            new() { Name = "Science", Id = "Science", Type = "Domain" },
            new() { Name = "Mathematics", Id = "Mathematics", Type = "Domain" },
            new() { Name = "Philosophy", Id = "Philosophy", Type = "Domain" },
            new() { Name = "Art", Id = "Art", Type = "Domain" },
            new() { Name = "Technology", Id = "Technology", Type = "Domain" },
            new() { Name = "History", Id = "History", Type = "Domain" },
            new() { Name = "Literature", Id = "Literature", Type = "Domain" },
            new() { Name = "Nature", Id = "Nature", Type = "Domain" },
            new() { Name = "Space", Id = "Space", Type = "Domain" },
            new() { Name = "Cognition", Id = "Cognition", Type = "Domain" }
        };
    }
}
