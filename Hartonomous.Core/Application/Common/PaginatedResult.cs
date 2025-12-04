namespace Hartonomous.Core.Application.Common;

/// <summary>
/// Generic paginated result for list queries
/// </summary>
public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public PaginatedResult()
    {
    }

    public PaginatedResult(List<T> items, int count, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = count;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
    }

    public static PaginatedResult<T> Create(List<T> items, int count, int pageNumber, int pageSize)
    {
        return new PaginatedResult<T>(items, count, pageNumber, pageSize);
    }
}
