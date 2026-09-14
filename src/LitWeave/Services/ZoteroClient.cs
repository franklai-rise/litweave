using System.Net;
using System.Net.Http;
using System.Text.Json;
using LitWeave.Models;

namespace LitWeave.Services;

public interface IZoteroReadClient
{
    Task<ZoteroStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<RefreshResult> RefreshAsync(ZoteroSnapshot? previous, CancellationToken cancellationToken = default);
}

public sealed class ZoteroUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ZoteroClient : IZoteroReadClient, IDisposable
{
    private readonly HttpClient _http;
    private const int PageSize = 100;
    private const int MaxPageAttempts = 3;
    private const int MaxStatusAttempts = 3;

    public ZoteroClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:23119/api/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Zotero-API-Version", "3");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LitWeave/0.2.0 (local desktop app)");
    }

    public async Task<ZoteroStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < MaxStatusAttempts; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync("", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var apiVersion = response.Headers.TryGetValues("Zotero-API-Version", out var values)
                    ? values.FirstOrDefault() : "3";
                var transient = response.StatusCode == HttpStatusCode.RequestTimeout
                    || response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (transient && attempt < MaxStatusAttempts - 1)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(180 * Math.Pow(2, attempt));
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var message = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Zotero's local API denied the request. Enable the local API in Zotero's Advanced settings.",
                        HttpStatusCode.NotFound => "Zotero's local API endpoint was not found. Make sure Zotero is running.",
                        HttpStatusCode.TooManyRequests => "Zotero's local API is busy. Please wait a moment and try again.",
                        _ when (int)response.StatusCode >= 500 => "Zotero's local API is temporarily unavailable. Please try again.",
                        _ => $"Zotero local API returned {(int)response.StatusCode} ({response.ReasonPhrase})."
                    };
                    return new ZoteroStatus
                    {
                        IsRunning = true,
                        ApiEnabled = false,
                        ApiVersion = apiVersion,
                        Message = message,
                        CheckedAt = checkedAt
                    };
                }

                return new ZoteroStatus
                {
                    IsRunning = true,
                    ApiEnabled = true,
                    ApiVersion = apiVersion,
                    ZoteroVersion = response.Headers.TryGetValues("X-Zotero-Version", out var versions)
                        ? versions.FirstOrDefault() : null,
                    Message = "Zotero local API is ready.",
                    CheckedAt = checkedAt
                };
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxStatusAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(180 * Math.Pow(2, attempt)), cancellationToken);
                    continue;
                }
                return new ZoteroStatus
                {
                    IsRunning = false,
                    ApiEnabled = false,
                    Message = "Zotero is not running or its local API is not reachable.",
                    CheckedAt = checkedAt
                };
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxStatusAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(180 * Math.Pow(2, attempt)), cancellationToken);
                    continue;
                }
                return new ZoteroStatus
                {
                    IsRunning = false,
                    ApiEnabled = false,
                    Message = "Timed out while checking the Zotero local API.",
                    CheckedAt = checkedAt
                };
            }
        }

        return new ZoteroStatus
        {
            IsRunning = false,
            ApiEnabled = false,
            Message = "Zotero is not running or its local API is not reachable.",
            CheckedAt = checkedAt
        };
    }

    public async Task<RefreshResult> RefreshAsync(ZoteroSnapshot? previous, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.ApiEnabled)
            throw new ZoteroUnavailableException(status.Message);

        try
        {
            var collectionElements = await ReadPagesAsync("users/0/collections", cancellationToken);
            var itemElements = await ReadPagesAsync("users/0/items", cancellationToken);
            var collections = collectionElements.Select(ParseCollection).Where(c => c is not null)
                .Cast<ZoteroCollection>().OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

            var raw = itemElements.Select(ParseRawItem).Where(x => x is not null).Cast<RawItem>().ToList();
            var attachmentsByParent = raw.Where(x => IsAttachment(x.ItemType) && !string.IsNullOrWhiteSpace(x.ParentItemKey))
                .GroupBy(x => x.ParentItemKey!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(ParseAttachment).Where(a => a is not null).Cast<ZoteroAttachment>()
                    .OrderBy(a => a.Key, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

            // Zotero's /items/top endpoint counts standalone imported PDFs as
            // top-level records. Keep those records visible too, while child
            // attachments and notes remain out of the literature card list.
            var items = raw.Where(x =>
                                      !string.Equals(x.ItemType, "note", StringComparison.OrdinalIgnoreCase) &&
                                      !string.Equals(x.ItemType, "annotation", StringComparison.OrdinalIgnoreCase) &&
                                      string.IsNullOrWhiteSpace(x.ParentItemKey))
                .Select(x => ParseItem(x, attachmentsByParent.TryGetValue(x.Key, out var attachments)
                    ? attachments : []))
                .OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
            foreach (var item in items) item.ContentHash = SnapshotHasher.ComputeItem(item);

            var snapshot = new ZoteroSnapshot
            {
                LibraryKey = "personal",
                RefreshedAt = DateTimeOffset.UtcNow,
                Collections = collections,
                Items = items
            };
            snapshot.ContentHash = SnapshotHasher.Compute(snapshot);
            var diff = BuildDiff(previous, snapshot);
            return new RefreshResult { Snapshot = snapshot, Diff = diff, Status = status };
        }
        catch (ZoteroUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ZoteroUnavailableException("The Zotero local API response could not be read.", ex);
        }
    }

    private async Task<List<JsonElement>> ReadPagesAsync(string path, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        for (var start = 0; ; start += PageSize)
        {
            var page = await GetJsonArrayAsync($"{path}?format=json&limit={PageSize}&start={start}", cancellationToken);
            result.AddRange(page);
            if (page.Count < PageSize) break;
        }
        return result;
    }

    private async Task<List<JsonElement>> GetJsonArrayAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxPageAttempts; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new ZoteroUnavailableException("Zotero's local API endpoint was not found.");
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ZoteroUnavailableException("Zotero's local API denied the request. Enable the local API in Zotero's Advanced settings.");

                var transient = response.StatusCode == HttpStatusCode.RequestTimeout
                    || response.StatusCode == HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (transient && attempt < MaxPageAttempts - 1)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(220 * Math.Pow(2, attempt));
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new ZoteroUnavailableException($"Zotero local API returned {(int)response.StatusCode} ({response.ReasonPhrase}).");

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Zotero API returned a non-array page.");
                return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();
            }
            catch (HttpRequestException) when (attempt < MaxPageAttempts - 1 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(220 * Math.Pow(2, attempt)), cancellationToken);
            }
            catch (TaskCanceledException) when (attempt < MaxPageAttempts - 1 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(220 * Math.Pow(2, attempt)), cancellationToken);
            }
        }

        throw new ZoteroUnavailableException("Zotero's local API could not be read after several attempts.");
    }

    private static ZoteroCollection? ParseCollection(JsonElement element)
    {
        var data = element.TryGetProperty("data", out var d) ? d : element;
        var key = StringProperty(element, "key") ?? StringProperty(data, "key");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var parent = data.TryGetProperty("parentCollection", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;
        var itemCount = data.TryGetProperty("numItems", out var directCount) && directCount.TryGetInt32(out var direct)
            ? direct
            : element.TryGetProperty("meta", out var meta) && meta.TryGetProperty("numItems", out var metaCount) && metaCount.TryGetInt32(out var count)
                ? count : 0;
        return new ZoteroCollection
        {
            Key = key,
            Name = StringProperty(data, "name") ?? key,
            ParentKey = parent,
            ItemCount = itemCount
        };
    }

    private sealed class RawItem
    {
        public string Key { get; init; } = string.Empty;
        public string ItemType { get; init; } = string.Empty;
        public string? ParentItemKey { get; init; }
        public JsonElement Data { get; init; }
        public JsonElement Root { get; init; }
    }

    private static RawItem? ParseRawItem(JsonElement element)
    {
        var data = element.TryGetProperty("data", out var d) ? d : element;
        var key = StringProperty(element, "key") ?? StringProperty(data, "key");
        var type = StringProperty(data, "itemType");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(type)) return null;
        return new RawItem
        {
            Key = key,
            ItemType = type,
            ParentItemKey = StringProperty(data, "parentItem"),
            Data = data.Clone(),
            Root = element.Clone()
        };
    }

    private static ZoteroItem ParseItem(RawItem raw, IReadOnlyList<ZoteroAttachment> attachments)
    {
        var data = raw.Data;
        var creators = new List<ZoteroCreator>();
        if (data.TryGetProperty("creators", out var creatorArray) && creatorArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var creator in creatorArray.EnumerateArray())
            {
                creators.Add(new ZoteroCreator
                {
                    CreatorType = StringProperty(creator, "creatorType") ?? string.Empty,
                    FirstName = StringProperty(creator, "firstName"),
                    LastName = StringProperty(creator, "lastName"),
                    Name = StringProperty(creator, "name")
                });
            }
        }

        var collections = data.TryGetProperty("collections", out var collectionArray) && collectionArray.ValueKind == JsonValueKind.Array
            ? collectionArray.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : [];
        var tags = data.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array
            ? tagArray.EnumerateArray().Select(t => StringProperty(t, "tag")).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : [];
        var extra = StringProperty(data, "extra");
        var storedCreator = creators.FirstOrDefault(c => c.CreatorType.Contains("correspond", StringComparison.OrdinalIgnoreCase))?.DisplayName;
        var storedExtra = ExtractCorrespondingAuthor(extra);
        var storedAffiliation = ExtractFirstAffiliation(extra);
        var date = StringProperty(data, "date");
        var itemAttachments = attachments.ToList();
        if (IsAttachment(raw.ItemType) && !itemAttachments.Any(a => a.Key == raw.Key))
        {
            itemAttachments.Insert(0, new ZoteroAttachment
            {
                Key = raw.Key,
                ParentItemKey = raw.Key,
                Title = StringProperty(data, "title") ?? StringProperty(data, "filename"),
                ContentType = StringProperty(data, "contentType")
            });
        }
        return new ZoteroItem
        {
            Key = raw.Key,
            ItemType = raw.ItemType,
            Title = StringProperty(data, "title") ?? "Untitled item",
            Date = date,
            Year = ExtractYear(date),
            PublicationTitle = StringProperty(data, "publicationTitle") ?? StringProperty(data, "bookTitle"),
            Doi = StringProperty(data, "DOI"),
            Url = StringProperty(data, "url"),
            AbstractNote = StringProperty(data, "abstractNote"),
            CorrespondingAuthor = storedCreator ?? storedExtra,
            FirstAffiliation = storedAffiliation,
            Creators = creators,
            CollectionKeys = collections,
            Tags = tags,
            Attachments = itemAttachments
        };
    }

    private static ZoteroAttachment? ParseAttachment(RawItem raw)
    {
        if (string.IsNullOrWhiteSpace(raw.ParentItemKey)) return null;
        return new ZoteroAttachment
        {
            Key = raw.Key,
            ParentItemKey = raw.ParentItemKey!,
            Title = StringProperty(raw.Data, "title"),
            ContentType = StringProperty(raw.Data, "contentType")
        };
    }

    private static RefreshDiff BuildDiff(ZoteroSnapshot? previous, ZoteroSnapshot current)
    {
        if (previous is null)
            return new RefreshDiff
            {
                IsBaseline = true,
                NewItemKeys = current.Items.Select(i => i.Key).ToList(),
                CollectionChanges = current.Collections.Select(c => c.Key).ToList()
            };
        var oldItems = previous.Items.ToDictionary(i => i.Key, StringComparer.Ordinal);
        var newItems = current.Items.ToDictionary(i => i.Key, StringComparer.Ordinal);
        return new RefreshDiff
        {
            NewItemKeys = newItems.Keys.Except(oldItems.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            RemovedItemKeys = oldItems.Keys.Except(newItems.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            UpdatedItemKeys = newItems.Keys.Intersect(oldItems.Keys, StringComparer.Ordinal)
                .Where(key => !string.Equals(newItems[key].ContentHash, oldItems[key].ContentHash, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToList(),
            CollectionChanges = current.Collections.Select(c => c.Key).Except(previous.Collections.Select(c => c.Key), StringComparer.Ordinal)
                .Concat(previous.Collections.Select(c => c.Key).Except(current.Collections.Select(c => c.Key), StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
        };
    }

    private static bool IsAttachment(string itemType) => string.Equals(itemType, "attachment", StringComparison.OrdinalIgnoreCase);

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ExtractYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(date, "(?<!\\d)(\\d{4})(?!\\d)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractCorrespondingAuthor(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(extra, "(?im)^\\s*Corresponding\\s+Author\\s*:\\s*(.+?)\\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractFirstAffiliation(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra)) return null;
        // Zotero has no canonical affiliation field. Only accept an explicitly
        // authored Extra line; never infer institutions from author order.
        var match = System.Text.RegularExpressions.Regex.Match(
            extra,
            "(?im)^\\s*(?:First\\s+(?:Affiliation|Institution)|Affiliation)\\s*:\\s*(.+?)\\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public void Dispose() => _http.Dispose();
}
