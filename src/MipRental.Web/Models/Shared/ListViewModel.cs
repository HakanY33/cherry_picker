namespace MipRental.Web.Models.Shared;

public class ListViewModel<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public PageInfo Page { get; init; } = new();
}
