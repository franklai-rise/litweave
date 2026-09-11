using LitWeave.Models;

namespace LitWeave.Services;

public sealed class CollectionTreeNode
{
    public ZoteroCollection Collection { get; init; } = new();
    public List<CollectionTreeNode> Children { get; } = [];
}

public static class CollectionTree
{
    /// <summary>Builds a safe tree from Zotero's flat Collection list.</summary>
    /// <remarks>Missing parents and cycles are promoted to roots so malformed
    /// local API data cannot make the UI recurse forever.</remarks>
    public static IReadOnlyList<CollectionTreeNode> Build(IEnumerable<ZoteroCollection> source)
    {
        var all = source.Where(c => !string.IsNullOrWhiteSpace(c.Key))
            .GroupBy(c => c.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToDictionary(c => c.Key, StringComparer.Ordinal);
        var nodes = all.Values.ToDictionary(c => c.Key, c => new CollectionTreeNode { Collection = c }, StringComparer.Ordinal);
        var roots = new List<CollectionTreeNode>();
        foreach (var collection in all.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(collection.ParentKey) || !nodes.ContainsKey(collection.ParentKey))
            {
                roots.Add(nodes[collection.Key]);
                continue;
            }
            if (WouldCycle(collection.Key, collection.ParentKey!, all))
            {
                roots.Add(nodes[collection.Key]);
                continue;
            }
            nodes[collection.ParentKey!].Children.Add(nodes[collection.Key]);
        }
        SortChildren(roots);
        return roots;
    }

    private static bool WouldCycle(string child, string parent, IReadOnlyDictionary<string, ZoteroCollection> all)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { child };
        var current = parent;
        while (all.TryGetValue(current, out var collection) && !string.IsNullOrWhiteSpace(collection.ParentKey))
        {
            if (!seen.Add(current)) return true;
            current = collection.ParentKey!;
        }
        return seen.Contains(current);
    }

    private static void SortChildren(IEnumerable<CollectionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.Children.Sort((left, right) => string.Compare(left.Collection.Name, right.Collection.Name, StringComparison.Ordinal));
            SortChildren(node.Children);
        }
    }
}
