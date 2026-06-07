using MDEdit.Core.Models;
using MDEdit.Infrastructure.Services;

namespace MDEdit.Tests;

public class MarkdownFormattingServiceTests
{
    private static MarkdownFormattingService CreateService() => new();

    [Fact]
    public void Format_KeepsHeadingsAndListsOnSeparateBlocks()
    {
        const string markdown = "# Title\n\n## Summary\n\nText\n\n- one\n- two\n";

        string result = CreateService().Format(markdown, MarkdownFlavor.GitHubFlavored);

        Assert.Contains("# Title", result);
        Assert.Contains("\n\n## Summary\n\n", result);
        Assert.Contains("\n\n- one\n- two", result);
    }

    [Fact]
    public void Format_PreservesFrontMatter()
    {
        const string markdown = "---\ntitle: Sample\n---\n\n# Heading\n\nBody text\n";

        string result = CreateService().Format(markdown, MarkdownFlavor.GitHubFlavored);

        Assert.StartsWith("---\ntitle: Sample\n---", result);
        Assert.Contains("# Heading", result);
    }

    [Fact]
    public void ReflowHeadings_DefaultsTopHierarchyToH1()
    {
        const string markdown = "## Root\n\n#### Child\n\n## Peer\n";

        MarkdownReflowResult result =
            CreateService().ReflowHeadings(markdown, MarkdownFlavor.GitHubFlavored, topHeadingLevel: 1);

        Assert.Contains("# Root", result.Markdown);
        Assert.Contains("## Child", result.Markdown);
        Assert.Contains("# Peer", result.Markdown);
        Assert.Equal(3, result.ChangedHeadingCount);
    }

    [Fact]
    public void ReflowHeadings_UsesSelectedTopHeadingLevel()
    {
        const string markdown = "## Root\n\n#### Child\n";

        MarkdownReflowResult result =
            CreateService().ReflowHeadings(markdown, MarkdownFlavor.GitHubFlavored, topHeadingLevel: 2);

        Assert.Contains("## Root", result.Markdown);
        Assert.Contains("### Child", result.Markdown);
    }

    [Fact]
    public void ReflowHeadings_PreservesFrontMatter()
    {
        const string markdown = "---\ntitle: Sample\n---\n\n## Heading\n\nBody text\n";

        MarkdownReflowResult result =
            CreateService().ReflowHeadings(markdown, MarkdownFlavor.GitHubFlavored, topHeadingLevel: 1);

        Assert.StartsWith("---\ntitle: Sample\n---", result.Markdown);
        Assert.Contains("# Heading", result.Markdown);
    }
}
