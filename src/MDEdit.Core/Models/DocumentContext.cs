namespace MDEdit.Core.Models;

public sealed class DocumentContext
{
    public string? FilePath { get; init; }
    public string RawMarkdown { get; init; } = string.Empty;
    public MarkdownFlavor Flavor { get; init; } = MarkdownFlavor.GitHubFlavored;

    public string DocumentTitle => string.IsNullOrWhiteSpace(FilePath)
        ? "Untitled"
        : Path.GetFileName(FilePath);
}
