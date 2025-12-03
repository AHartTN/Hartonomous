using Hartonomous.Shared.Models;

namespace Hartonomous.Shared.Interfaces;

public interface IAtomService
{
    Task<AtomDto?> GetAtomByIdAsync(long id);
    Task<AtomDto?> GetAtomByHashAsync(string contentHash);
    Task<List<AtomDto>> GetAtomsByTypeAsync(string atomType, int skip = 0, int take = 100);
    Task<AtomDto> CreateAtomAsync(CreateAtomRequest request);
    Task<bool> DeleteAtomAsync(long id);
}
