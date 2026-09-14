using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LitWeave.Models;

public sealed class ZoteroCollection
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentKey { get; set; }
    public int ItemCount { get; set; }
}

public sealed class ZoteroCreator
{
    public string CreatorType { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Name { get; set; }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name.Trim();
            var first = FirstName?.Trim() ?? string.Empty;
            var last = LastName?.Trim() ?? string.Empty;
            return string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}

public sealed class ZoteroAttachment
{
    public string Key { get; set; } = string.Empty;
    public string ParentItemKey { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? ContentType { get; set; }

    public bool IsPdf => string.Equals(ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(ContentType, "application/x-pdf", StringComparison.OrdinalIgnoreCase);
}

public sealed class ZoteroItem
{
    public string Key { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = "Untitled item";
    public string? Date { get; set; }
    public string? Year { get; set; }
    public string? PublicationTitle { get; set; }
    public string? Doi { get; set; }
    public string? Url { get; set; }
    public string? AbstractNote { get; set; }
    public string? CorrespondingAuthor { get; set; }
    public string? FirstAffiliation { get; set; }
    public List<ZoteroCreator> Creators { get; set; } = [];
    public List<string> CollectionKeys { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<ZoteroAttachment> Attachments { get; set; } = [];
    public string ContentHash { get; set; } = string.Empty;

    public string? FirstAuthor => Creators.FirstOrDefault(c =>
        string.Equals(c.CreatorType, "author", StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? Creators.FirstOrDefault()?.DisplayName;

    public bool HasPdf => Attachments.Any(a => a.IsPdf);
    public IEnumerable<ZoteroAttachment> PdfAttachments => Attachments.Where(a => a.IsPdf);
}

public sealed class ZoteroSnapshot
{
    public string LibraryKey { get; set; } = "personal";
    public DateTimeOffset RefreshedAt { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public List<ZoteroCollection> Collections { get; set; } = [];
    public List<ZoteroItem> Items { get; set; } = [];
}

public sealed class RefreshDiff
{
    public List<string> NewItemKeys { get; set; } = [];
    public List<string> UpdatedItemKeys { get; set; } = [];
    public List<string> RemovedItemKeys { get; set; } = [];
    public List<string> CollectionChanges { get; set; } = [];
    public bool IsBaseline { get; set; }
}

public sealed class RefreshResult
{
    public ZoteroSnapshot Snapshot { get; set; } = new();
    public RefreshDiff Diff { get; set; } = new();
    public ZoteroStatus Status { get; set; } = new();
}

public sealed class ZoteroStatus
{
    public bool IsRunning { get; set; }
    public bool ApiEnabled { get; set; }
    public string? ZoteroVersion { get; set; }
    public string? ApiVersion { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CheckedAt { get; set; }
}

public static class SnapshotHasher
{
    public static string Compute(ZoteroSnapshot snapshot)
    {
        var sb = new StringBuilder();
        foreach (var collection in snapshot.Collections.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            sb.Append("c|").Append(collection.Key).Append('|').Append(collection.Name).Append('|')
                .Append(collection.ParentKey).Append('|').Append(collection.ItemCount).Append('\n');
        }

        foreach (var item in snapshot.Items.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            sb.Append("i|").Append(item.Key).Append('|').Append(item.ItemType).Append('|')
                .Append(item.Title).Append('|').Append(item.Date).Append('|').Append(item.PublicationTitle)
                .Append('|').Append(item.Doi).Append('|').Append(item.CorrespondingAuthor).Append('|').Append(item.FirstAffiliation).Append('|')
                .Append(string.Join(',', item.CollectionKeys.Order(StringComparer.Ordinal))).Append('|')
                .Append(string.Join(',', item.Tags.Order(StringComparer.Ordinal))).Append('|');
            foreach (var creator in item.Creators)
                sb.Append(creator.CreatorType).Append(':').Append(creator.DisplayName).Append(';');
            sb.Append('|');
            foreach (var attachment in item.Attachments.OrderBy(a => a.Key, StringComparer.Ordinal))
                sb.Append(attachment.Key).Append(':').Append(attachment.ContentType).Append(';');
            sb.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static string ComputeItem(ZoteroItem item)
    {
        var wrapper = new ZoteroSnapshot { Collections = [], Items = [item] };
        return Compute(wrapper);
    }
}
