using LitWeave.Models;
using LitWeave.Services;
using Xunit;

namespace LitWeave.Tests;

public sealed class ModelTests
{
    [Fact]
    public void CreatorDisplayNameHonorsSingleNameAndFamilyName()
    {
        var creator = new ZoteroCreator { CreatorType = "author", FirstName = "Ada", LastName = "Lovelace" };
        Assert.Equal("Ada Lovelace", creator.DisplayName);

        var organization = new ZoteroCreator { CreatorType = "author", Name = "OpenAI" };
        Assert.Equal("OpenAI", organization.DisplayName);
    }

    [Fact]
    public void SnapshotHashIsStableForEquivalentOrdering()
    {
        var first = new ZoteroSnapshot
        {
            Collections = [new ZoteroCollection { Key = "b", Name = "B" }, new ZoteroCollection { Key = "a", Name = "A" }],
            Items = [new ZoteroItem { Key = "z", Title = "Z" }, new ZoteroItem { Key = "a", Title = "A" }]
        };
        var second = new ZoteroSnapshot
        {
            Collections = [new ZoteroCollection { Key = "a", Name = "A" }, new ZoteroCollection { Key = "b", Name = "B" }],
            Items = [new ZoteroItem { Key = "a", Title = "A" }, new ZoteroItem { Key = "z", Title = "Z" }]
        };
        Assert.Equal(SnapshotHasher.Compute(first), SnapshotHasher.Compute(second));
    }

    [Fact]
    public void PdfAttachmentIsDistinguishedFromParentItem()
    {
        var item = new ZoteroItem
        {
            Key = "PARENT",
            Attachments = [new ZoteroAttachment { Key = "PDFKEY", ParentItemKey = "PARENT", ContentType = "application/pdf" }]
        };
        Assert.True(item.HasPdf);
        Assert.Equal("PDFKEY", item.PdfAttachments.Single().Key);
        Assert.NotEqual(item.Key, item.PdfAttachments.Single().Key);
    }

    [Fact]
    public async Task DisabledSuggestionsNeverCreateRelations()
    {
        var provider = new DisabledRelationshipSuggestionProvider();
        var result = await provider.SuggestAsync([]);
        Assert.Empty(result);
    }

    [Fact]
    public void CollectionTreePromotesOrphansAndCyclesSafely()
    {
        var roots = CollectionTree.Build([
            new ZoteroCollection { Key = "a", Name = "A", ParentKey = "missing" },
            new ZoteroCollection { Key = "b", Name = "B", ParentKey = "c" },
            new ZoteroCollection { Key = "c", Name = "C", ParentKey = "b" },
            new ZoteroCollection { Key = "child", Name = "Child", ParentKey = "a" },
        ]);
        Assert.Equal(3, roots.Count);
        Assert.Contains(roots, root => root.Collection.Key == "a");
        Assert.Contains(roots, root => root.Collection.Key == "b");
        Assert.Contains(roots, root => root.Collection.Key == "c");
        Assert.Single(roots.Single(root => root.Collection.Key == "a").Children);
    }

    [Fact]
    public void RepositoryRoundTripsCanvasAndSnapshotInTransactions()
    {
        var directory = Directory.CreateTempSubdirectory("litweave-test-");
        var database = Path.Combine(directory.FullName, "canvas.db");
        try
        {
            using (var repository = new LitWeaveRepository(database))
            {
                var snapshot = new ZoteroSnapshot { ContentHash = "hash", RefreshedAt = DateTimeOffset.UtcNow };
                repository.SaveSnapshot(snapshot, new RefreshDiff { IsBaseline = true });
                repository.SaveCanvas(new CanvasDocument { RootCollectionKey = "root", Nodes = [new CanvasNode { Id = "paper:P" }] });
            }
            using (var reopened = new LitWeaveRepository(database))
            {
                Assert.Equal("hash", reopened.LoadSnapshot()?.ContentHash);
                Assert.Equal("paper:P", reopened.LoadCanvas("root")?.Nodes.Single().Id);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
