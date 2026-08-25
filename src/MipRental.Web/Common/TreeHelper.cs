namespace MipRental.Web.Common;

public static class TreeHelper
{
    /// <summary>
    /// parentId, id'nin kendisiyse ya da id'nin alt ağacındaki bir düğümse true döner
    /// (kendini kendine parent yapma / döngü engeli).
    /// </summary>
    public static bool WouldCreateCycle<T>(
        IEnumerable<T> allNodes, int id, int? parentId, Func<T, int> getId, Func<T, int?> getParentId)
    {
        if (parentId is null)
        {
            return false;
        }

        if (parentId == id)
        {
            return true;
        }

        var byId = allNodes.ToDictionary(getId);
        int? current = parentId;
        var visited = new HashSet<int>();

        while (current is not null)
        {
            if (current == id)
            {
                return true;
            }

            if (!visited.Add(current.Value))
            {
                return false; // ilgisiz bir döngüye rastladık, id burada yok
            }

            current = byId.TryGetValue(current.Value, out var node) ? getParentId(node) : null;
        }

        return false;
    }
}
