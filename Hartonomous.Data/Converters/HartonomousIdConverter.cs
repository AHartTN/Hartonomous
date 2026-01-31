using Hartonomous.Core.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hartonomous.Data.Converters;

public class HartonomousIdConverter : ValueConverter<HartonomousId, Guid>
{
    public HartonomousIdConverter() 
        : base(
            id => id.ToGuid(),
            guid => HartonomousId.FromGuid(guid))
    {
    }
}
