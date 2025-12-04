namespace Hartonomous.Core.Domain.Exceptions;

/// <summary>
/// Exception thrown when an invalid spatial coordinate is encountered
/// </summary>
public sealed class InvalidSpatialCoordinateException : DomainException
{
    public double? X { get; }
    public double? Y { get; }
    public double? Z { get; }
    
    public InvalidSpatialCoordinateException(string message, double? x = null, double? y = null, double? z = null)
        : base(message)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
