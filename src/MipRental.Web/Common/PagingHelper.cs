using Microsoft.EntityFrameworkCore;
using MipRental.Web.Models.Shared;

namespace MipRental.Web.Common;

public static class PagingHelper
{
    public const int PageSize = 25;

    public static async Task<ListViewModel<T>> ToPagedListAsync<T>(
        this IQueryable<T> query, int page, string? search, bool showInactive)
    {
        page = page < 1 ? 1 : page;

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();

        return new ListViewModel<T>
        {
            Items = items,
            Page = new PageInfo
            {
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize),
                Search = search,
                ShowInactive = showInactive
            }
        };
    }
}
