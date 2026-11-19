using TinaClass.Contracts.Common;

namespace TinaClass.Api.Common.Http;

/// <summary>The paging of every list (design 7): <c>?page=&amp;pageSize=</c>, 30 by default and at most 100.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultSize = 30;
    public const int MaxSize = 100;

    public static PageRequest Of(int? page, int? pageSize) => new(Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultSize, 1, MaxSize));

    public int Skip => (Page - 1) * PageSize;

    public Paged<T> Wrap<T>(IReadOnlyList<T> items, int total) => new(items, total, Page, PageSize);
}
