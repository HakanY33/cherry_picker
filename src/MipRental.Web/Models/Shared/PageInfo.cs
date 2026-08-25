namespace MipRental.Web.Models.Shared;

public class PageInfo
{
    public int CurrentPage { get; init; } = 1;
    public int TotalPages { get; init; }
    public string? Search { get; init; }
    public bool ShowInactive { get; init; }
}
