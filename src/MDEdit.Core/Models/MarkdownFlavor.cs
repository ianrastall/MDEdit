namespace MDEdit.Core.Models;

public enum MarkdownFlavor
{
    CommonMark,
    GitHubFlavored,
    MarkdigAdvanced,
    PandocMarkdown,
    MultiMarkdown,
}

public sealed record MarkdownFlavorOption(MarkdownFlavor Flavor, string DisplayName);

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

    public static IReadOnlyList<MarkdownFlavorOption> Options { get; } =
        All.Select(flavor => new MarkdownFlavorOption(flavor, DisplayName(flavor))).ToArray();

    public static string DisplayName(MarkdownFlavor flavor) => flavor switch
    {
        MarkdownFlavor.CommonMark => "CommonMark",
        MarkdownFlavor.GitHubFlavored => "GitHub Flavored Markdown",
        MarkdownFlavor.MarkdigAdvanced => "Markdig Advanced",
        MarkdownFlavor.PandocMarkdown => "Pandoc Markdown",
        MarkdownFlavor.MultiMarkdown => "MultiMarkdown",
        _ => flavor.ToString(),
    };

    public static MarkdownFlavorOption OptionFor(MarkdownFlavor flavor) =>
        Options.FirstOrDefault(option => option.Flavor == flavor)
        ?? new MarkdownFlavorOption(flavor, DisplayName(flavor));
}
