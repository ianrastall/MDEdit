namespace MDEdit.Core.Models;

public enum MarkdownFlavor
{
    CommonMark,
    GitHubFlavored,
    MarkdigAdvanced,
    PandocMarkdown,
    MultiMarkdown,
}

public static class MarkdownFlavorCatalog
{
    public static IReadOnlyList<MarkdownFlavor> All { get; } =
    [
        MarkdownFlavor.CommonMark,
        MarkdownFlavor.GitHubFlavored,
        MarkdownFlavor.MarkdigAdvanced,
        MarkdownFlavor.PandocMarkdown,
        MarkdownFlavor.MultiMarkdown,
    ];

    public static string DisplayName(MarkdownFlavor flavor) => flavor switch
    {
        MarkdownFlavor.CommonMark => "CommonMark",
        MarkdownFlavor.GitHubFlavored => "GitHub Flavored Markdown",
        MarkdownFlavor.MarkdigAdvanced => "Markdig Advanced",
        MarkdownFlavor.PandocMarkdown => "Pandoc Markdown",
        MarkdownFlavor.MultiMarkdown => "MultiMarkdown",
        _ => flavor.ToString(),
    };
}
