namespace LitWeave.Models;

public sealed class CanvasDocument
{
    public string Id { get; set; } = string.Empty;
    public string RootCollectionKey { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;
    public int LayoutVersion { get; set; } = 1;
    public List<CanvasNode> Nodes { get; set; } = [];
    public List<CanvasEdge> Edges { get; set; } = [];
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
    public string? Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 150;
    public bool IsAlias { get; set; }
    public bool IsGhost { get; set; }
    public bool IsOutOfScope { get; set; }
}

public sealed class CanvasEdge
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Note { get; set; }
    public bool IsDirected { get; set; } = true;
    public string Color { get; set; } = "#7C9CFF";
    public string LineStyle { get; set; } = "solid";
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
