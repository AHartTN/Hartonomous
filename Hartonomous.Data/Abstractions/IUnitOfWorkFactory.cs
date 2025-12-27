namespace Hartonomous.Data.Abstractions;

/// <summary>
/// Factory for creating unit of work instances.
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Create a new unit of work.
    /// </summary>
    IUnitOfWork Create();
}
