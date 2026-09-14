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

    [Fact]
    public void IntegrityCheckAcceptsAHealthyNewDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("litweave-integrity-test-");
        var database = Path.Combine(directory.FullName, "healthy.db");
        try
        {
            using (var repository = new LitWeaveRepository(database))
                repository.CreateBoard("Healthy board");
            Assert.Null(LitWeaveRepository.GetIntegrityError(database));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void RepositoryCreatesIndependentBoardsAndRetainsTheirNames()
    {
        var directory = Directory.CreateTempSubdirectory("litweave-board-test-");
        var database = Path.Combine(directory.FullName, "boards.db");
        try
        {
            string firstId;
            using (var repository = new LitWeaveRepository(database))
            {
                var first = repository.CreateBoard("Mechanism map");
                var second = repository.CreateBoard("Methods map");
                firstId = first.Id;
                Assert.NotEqual(first.Id, second.Id);
                Assert.All(repository.ListBoards(), board => Assert.StartsWith("board:", board.Id));
                Assert.Equal(2, repository.ListBoards().Count);
            }
            using (var reopened = new LitWeaveRepository(database))
            {
                Assert.Equal("Mechanism map", reopened.LoadBoard(firstId)?.Name);
                Assert.Equal(2, reopened.ListBoards().Count);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void SnapshotHashTracksExplicitFirstAffiliation()
    {
        var item = new ZoteroItem { Key = "P", Title = "Paper", FirstAffiliation = "University A" };
        var first = SnapshotHasher.ComputeItem(item);
        item.FirstAffiliation = "University B";
        Assert.NotEqual(first, SnapshotHasher.ComputeItem(item));
    }

    [Fact]
    public void LegacyCollectionCanvasIsCopiedIntoAnIndependentBoardWithBackup()
    {
        var directory = Directory.CreateTempSubdirectory("litweave-migration-test-");
        var database = Path.Combine(directory.FullName, "legacy.db");
        try
        {
            using (var repository = new LitWeaveRepository(database))
            {
                repository.SaveCanvas(new CanvasDocument
                {
                    Id = "personal:root",
                    RootCollectionKey = "root",
                    Nodes = [new CanvasNode { Id = "paper:legacy", Kind = "paper" }]
                });
                repository.SetSetting("v2-board-migration", "pending");
            }
            using (var reopened = new LitWeaveRepository(database))
            {
                var converted = reopened.LoadBoard("board:legacy:root");
                Assert.NotNull(converted);
                Assert.True(converted!.IsLegacy);
                Assert.Equal("paper:legacy", converted.Nodes.Single().Id);
                Assert.Equal("paper:legacy", reopened.LoadCanvas("root")?.Nodes.Single().Id);
            }
            Assert.Single(Directory.GetFiles(Path.Combine(directory.FullName, "backups"), "*.db"));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void ImageAssetsAreStoredBesideTheConfiguredDatabase()
    {
        var directory = Directory.CreateTempSubdirectory("litweave-image-test-");
        try
        {
            using var repository = new LitWeaveRepository(Path.Combine(directory.FullName, "board.db"));
            const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlE4L0AAAAASUVORK5CYII=";
            var imageId = repository.SaveImage(png);
            Assert.True(File.Exists(Path.Combine(directory.FullName, "images", imageId)));
            Assert.StartsWith("data:image/png;base64,", repository.GetImageDataUrl(imageId));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void BoardSourceRoundTripsImagesAndReferencedMetadataWithoutReplacingTheOriginal()
    {
        var directory = Directory.CreateTempSubdirectory("litweave-source-test-");
        try
        {
            var database = Path.Combine(directory.FullName, "board.db");
            var package = Path.Combine(directory.FullName, "portable.litweave");
            using var repository = new LitWeaveRepository(database);
            repository.SaveSnapshot(new ZoteroSnapshot
            {
                ContentHash = "metadata",
                RefreshedAt = DateTimeOffset.UtcNow,
                Items = [new ZoteroItem { Key = "P", Title = "Portable metadata", Creators = [new ZoteroCreator { CreatorType = "author", Name = "A. Author" }] }]
            }, new RefreshDiff { IsBaseline = true });
            const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlE4L0AAAAASUVORK5CYII=";
            var imageId = repository.SaveImage(png);
            var board = repository.CreateBoard("Portable board");
            board.Nodes = [
                new CanvasNode { Id = "paper:P", Kind = "paper", ItemKey = "P", DisplayName = "#1" },
                new CanvasNode { Id = "image:1", Kind = "image", ImageId = imageId, DisplayName = "#2" }
            ];
            board.Edges = [new CanvasEdge { Id = "edge:1", Source = "paper:P", Target = "image:1", Label = "supports" }];
            repository.SaveCanvas(board);
            repository.WriteBoardPackage(board, package);

            var imported = repository.ImportBoardPackage(package);

            Assert.NotEqual(board.Id, imported.Id);
            Assert.Equal("Portable board (imported)", imported.Name);
            Assert.Equal("Portable metadata", imported.ItemMetadata.Single().Title);
            Assert.Single(imported.Edges);
            var importedImage = imported.Nodes.Single(node => node.Kind == "image").ImageId;
            Assert.NotEqual(imageId, importedImage);
            Assert.StartsWith("data:image/png;base64,", repository.GetImageDataUrl(importedImage!));
            Assert.NotNull(repository.LoadBoard(board.Id));
            Assert.NotNull(repository.LoadBoard(imported.Id));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
