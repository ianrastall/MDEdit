using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using MDEdit.Core.Interfaces;
using MDEdit.Core.Models;

namespace MDEdit.Infrastructure.Services;

public sealed class MarkdownFormattingService : IMarkdownFormattingService
{
    public string Format(string rawMarkdown, MarkdownFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(rawMarkdown);

        MarkdownParts parts = SplitFrontMatter(rawMarkdown);
        MarkdownPipeline pipeline = CreatePipeline(flavor);

        try
        {
            MarkdownDocument document = Markdig.Markdown.Parse(parts.Body, pipeline);
            NormalizeHeadingsToAtx(document);
            RemoveSyntheticHeadingDefinitions(document);
            return ReattachFrontMatter(parts.FrontMatter, RenderNormalized(document, pipeline));
        }
        catch (ArgumentException)
        {
            return rawMarkdown;
        }
    }

    private static MarkdownPipeline CreatePipeline(MarkdownFlavor flavor)
    {
        var builder = new MarkdownPipelineBuilder();

        return flavor == MarkdownFlavor.CommonMark
            ? builder.Build()
            : builder.UseAdvancedExtensions().Build();
    }

    private static MarkdownParts SplitFrontMatter(string markdown)
    {
        string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new(null, markdown);
        }

        int closing = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closing < 0)
        {
            return new(null, markdown);
        }

        int bodyStart = closing + "\n---\n".Length;
        return new(normalized[..bodyStart].TrimEnd(), normalized[bodyStart..]);
    }

    private static string ReattachFrontMatter(string? frontMatter, string body)
    {
        return string.IsNullOrWhiteSpace(frontMatter)
            ? body
            : frontMatter + "\n\n" + body.TrimStart();
    }

    private static string RenderNormalized(MarkdownDocument document, MarkdownPipeline pipeline)
    {
        var options = new NormalizeOptions
        {
            SpaceAfterQuoteBlock = true,
            EmptyLineAfterCodeBlock = true,
            EmptyLineAfterHeading = true,
            EmptyLineAfterThematicBreak = true,
            ListItemCharacter = '-',
            ExpandAutoLinks = true,
        };

        using var writer = new StringWriter();
        var renderer = new NormalizeRenderer(writer, options);
        pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static void NormalizeHeadingsToAtx(MarkdownDocument document)
    {
        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            heading.IsSetext = false;
            heading.HeaderChar = '#';
            heading.HeaderCharCount = Math.Clamp(heading.Level, 1, 6);
        }
    }

    private static void RemoveSyntheticHeadingDefinitions(MarkdownDocument document)
    {
        foreach (LinkReferenceDefinitionGroup group in
            document.Descendants<LinkReferenceDefinitionGroup>().ToArray())
        {
            foreach (HeadingLinkReferenceDefinition definition in
                group.OfType<HeadingLinkReferenceDefinition>().ToArray())
            {
                group.Remove(definition);
            }

            if (group.Count == 0)
            {
                group.Parent?.Remove(group);
            }
        }
    }

    private sealed record MarkdownParts(string? FrontMatter, string Body);
}
