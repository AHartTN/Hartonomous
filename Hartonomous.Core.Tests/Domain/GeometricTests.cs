using Hartonomous.Core.Domain.Mathematics;
using Hartonomous.Core.Domain.Utilities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Marshal;
using NetTopologySuite.Geometries;
using Xunit;

namespace Hartonomous.Core.Tests.Domain;

public class GeometricTests
{
    [Fact]
    public void HilbertCurve4D_EncodeDecode_IsReversible()
    {
        // Test random coordinates within precision range
        uint maxVal = (1u << 21) - 1;
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            uint x = (uint)random.NextInt64(maxVal);
            uint y = (uint)random.NextInt64(maxVal);
            uint z = (uint)random.NextInt64(maxVal);
            uint m = (uint)random.NextInt64(maxVal);

            var (high, low) = HilbertCurve4D.Encode(x, y, z, m, 21);
            var decoded = HilbertCurve4D.Decode(high, low, 21);

            Assert.Equal(x, decoded.X);
            Assert.Equal(y, decoded.Y);
            Assert.Equal(z, decoded.Z);
            Assert.Equal(m, decoded.M);
        }
    }

    [Fact]
    public void GramSchmidtProjector_IsDeterministic()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("Hello World");
        var hash = Hash256.Compute(data);

        var p1 = GramSchmidtProjector.Project(hash, "Text", 21);
        var p2 = GramSchmidtProjector.Project(hash, "Text", 21);

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void GramSchmidtProjector_ModalityAffectsProjection()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("Hello World");
        var hash = Hash256.Compute(data);

        var pText = GramSchmidtProjector.Project(hash, "Text", 21);
        var pImage = GramSchmidtProjector.Project(hash, "Image", 21);

        Assert.NotEqual(pText, pImage);
    }

    [Fact]
    public void SpatialCoordinate_Interpolate_AveragesCorrectly()
    {
        var c1 = SpatialCoordinate.FromUniversalProperties(100, 100, 100, 100, 21);
        var c2 = SpatialCoordinate.FromUniversalProperties(200, 200, 200, 200, 21);

        var centroid = SpatialCoordinate.Interpolate(new[] { c1, c2 });

        // Expected average is 150
        Assert.Equal(150.0, centroid.X);
        Assert.Equal(150.0, centroid.Y);
        Assert.Equal(150.0, centroid.Z);
        Assert.Equal(150.0, centroid.M);
    }
}
