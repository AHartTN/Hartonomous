using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class Atom
{
    public HartonomousId Id { get; set; }
    public uint Codepoint { get; set; }
    public HartonomousId PhysicalityId { get; set; }
}
