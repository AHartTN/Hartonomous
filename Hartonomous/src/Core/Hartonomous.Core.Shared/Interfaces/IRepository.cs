namespace Hartonomous.Core.Shared.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, string userId);
    Task<IEnumerable<T>> GetAllAsync(string userId);
    Task<Guid> CreateAsync(T entity, string userId);
    Task<bool> UpdateAsync(T entity, string userId);
    Task<bool> DeleteAsync(Guid id, string userId);
}