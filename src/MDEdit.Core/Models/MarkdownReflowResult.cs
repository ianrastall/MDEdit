namespace MDEdit.Core.Models;

public sealed record MarkdownReflowResult(
    string Markdown,
    int ChangedHeadingCount,
    IReadOnlyList<string> Warnings);
