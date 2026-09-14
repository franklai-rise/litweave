namespace LitWeave.Models;

public sealed class CanvasDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Untitled board";
    public bool IsLegacy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    // Retained solely to read the v0.1 collection-bound canvas format.
    public string RootCollectionKey { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;
    public int LayoutVersion { get; set; } = 1;
    // Per-board sequence used for the neutral #1, #2 ... display names.
    public int NextNodeNumber { get; set; } = 1;
    public bool ShowAllDetails { get; set; }
    public List<CanvasNode> Nodes { get; set; } = [];
    public List<CanvasEdge> Edges { get; set; } = [];
    // A portable board carries only the metadata for papers that appear on it.
    // The local Zotero cache wins when it has newer data.
    public List<ZoteroItem> ItemMetadata { get; set; } = [];
    public List<CanvasTextNode> TextNodes { get; set; } = [];
    public double ViewportX { get; set; }
    public double ViewportY { get; set; }
    public double ViewportZoom { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanvasNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "paper";
    public string? ItemKey { get; set; }
    public string? CollectionKey { get; set; }
    public string? Title { get; set; }
    public string? DisplayName { get; set; }
    public string? Text { get; set; }
    public string? ImageId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 150;
    public int ZIndex { get; set; } = 1;
    public bool IsAlias { get; set; }
    public bool IsGhost { get; set; }
    public bool IsOutOfScope { get; set; }
    public NodeAppearance Appearance { get; set; } = new();
    // This is the user's preferred size. The canvas may temporarily reduce it
    // while a node is too small, but never persists that derived value.
    public double TitleFontSize { get; set; } = 14;
}

public sealed class NodeAppearance
{
    public string BorderColor { get; set; } = "#7182A8";
    public double BorderWidth { get; set; } = 1.5;
    public string FillColor { get; set; } = "#FFFFFF";
}

public sealed class CanvasEdge
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? SourceHandle { get; set; }
    public string? TargetHandle { get; set; }
    public string? Label { get; set; }
    public string? Note { get; set; }
    public bool IsDirected { get; set; } = true;
    public string Color { get; set; } = "#7C9CFF";
    public string LineStyle { get; set; } = "solid";
    public double Width { get; set; } = 2;
}

public sealed class BoardSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsLegacy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CanvasTextNode
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 100;
}

public sealed class RelationshipSuggestion
{
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public double Confidence { get; set; }
}

public interface IRelationshipSuggestionProvider
{
    Task<IReadOnlyList<RelationshipSuggestion>> SuggestAsync(
        IReadOnlyList<ZoteroItem> selectedItems,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledRelationshipSuggestionProvider : IRelationshipSuggestionProvider
{
    public Task<IReadOnlyList<RelationshipSuggestion>> SuggestAsync(
        IReadOnlyList<ZoteroItem> selectedItems,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RelationshipSuggestion>>([]);
}
