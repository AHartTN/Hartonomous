using System.ComponentModel.DataAnnotations.Schema;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class Physicality
{
    public HartonomousId Id { get; set; }
    
    // Storing as string or BigInteger because native UInt128 support in EF Core 
    // might still map to numeric(38) or uuid dependent on provider.
    // Given the C++ sent "0" string, we'll use a numeric backing or string for now.
    // Ideally this maps to a NUMERIC(39,0) column.
    public UInt128 HilbertIndex { get; set; }
    
    // WKT representation for now, until NTS is fully integrated.
    [Column(TypeName = "geometry")]
    public string Centroid { get; set; } = string.Empty;
}
