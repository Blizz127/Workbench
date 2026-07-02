namespace Workbench.Core.Models;

/// <summary>Free-form markdown note attached to a project. Searchable.</summary>
public sealed class Note
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown body.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
