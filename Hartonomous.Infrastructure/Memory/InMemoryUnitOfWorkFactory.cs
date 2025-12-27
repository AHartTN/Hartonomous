using Hartonomous.Data.Abstractions;

namespace Hartonomous.Infrastructure.Memory;

/// <summary>
/// Factory for creating in-memory unit of work instances.
/// Uses shared repository instances for the lifetime of the factory.
/// </summary>
public sealed class InMemoryUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly InMemoryAtomRepository _atoms = new();
    private readonly InMemoryCompositionRepository _compositions = new();

    public IUnitOfWork Create()
    {
        return new InMemoryUnitOfWork(_atoms, _compositions);
    }
}
