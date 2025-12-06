using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Data.IntegrationTests.Builders;

/// <summary>
/// Fluent builder for creating test Constant entities with configurable properties.
/// Simplifies test data creation with sensible defaults.
/// </summary>
public class ConstantBuilder
{
    private byte[] _data = new byte[] { 0x01, 0x02, 0x03 };
    private ContentType _contentType = ContentType.Binary;
    private SpatialCoordinate? _coordinate;
    private ConstantStatus? _status;
    private long _referenceCount = 0;
    private long _frequency = 1;

    public ConstantBuilder WithData(byte[] data)
    {
        _data = data;
        return this;
    }

    public ConstantBuilder WithContentType(ContentType contentType)
    {
        _contentType = contentType;
        return this;
    }

    public ConstantBuilder WithCoordinate(int y, int z, int m)
    {
        _coordinate = SpatialCoordinate.FromUniversalProperties(0, y, z, m);
        return this;
    }

    public ConstantBuilder WithCoordinate(SpatialCoordinate coordinate)
    {
        _coordinate = coordinate;
        return this;
    }

    public ConstantBuilder WithStatus(ConstantStatus status)
    {
        _status = status;
        return this;
    }

    public ConstantBuilder Active()
    {
        _status = ConstantStatus.Active;
        return this;
    }

    public ConstantBuilder Projected()
    {
        _status = ConstantStatus.Projected;
        return this;
    }

    public ConstantBuilder WithReferenceCount(long count)
    {
        _referenceCount = count;
        return this;
    }

    public ConstantBuilder WithFrequency(long frequency)
    {
        _frequency = frequency;
        return this;
    }

    public Constant Build()
    {
        var constant = Constant.Create(_data, _contentType);

        // Apply coordinate if specified
        if (_coordinate != null)
        {
            constant.SetCoordinateForTesting(_coordinate);
        }

        // Apply status
        if (_status.HasValue)
        {
            if (_status == ConstantStatus.Active)
            {
                constant.ActivateForTesting();
            }
            else if (_status == ConstantStatus.Projected && _coordinate == null)
            {
                // Auto-project with defaults if not already set
                constant.Project();
            }
        }

        // Apply reference count and frequency via reflection or public methods
        for (int i = 0; i < _referenceCount; i++)
        {
            constant.IncrementReferenceCount();
        }

        for (int i = 1; i < _frequency; i++) // Start at 1 since Create sets frequency to 1
        {
            constant.IncrementFrequency();
        }

        return constant;
    }

    /// <summary>
    /// Creates multiple constants with sequential data bytes.
    /// </summary>
    public static IEnumerable<Constant> BuildMany(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new ConstantBuilder()
                .WithData(new byte[] { (byte)i })
                .Build();
        }
    }
}
