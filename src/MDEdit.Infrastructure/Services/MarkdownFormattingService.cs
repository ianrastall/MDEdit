using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using MDEdit.Core.Interfaces;
using MDEdit.Core.Models;
using System.Text.RegularExpressions;

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

    public MarkdownReflowResult ReflowHeadings(
        string rawMarkdown,
        MarkdownFlavor flavor,
        int topHeadingLevel)
    {
        ArgumentNullException.ThrowIfNull(rawMarkdown);

        topHeadingLevel = Math.Clamp(topHeadingLevel, 1, 6);
        MarkdownParts parts = SplitFrontMatter(rawMarkdown);
        MarkdownPipeline pipeline = CreatePipeline(flavor);
        MarkdownDocument document = Markdig.Markdown.Parse(parts.Body, pipeline);
        IReadOnlyList<HeadingChange> changes = NormalizeHeadingHierarchy(document, topHeadingLevel);
        RemoveSyntheticHeadingDefinitions(document);

        string markdown = ReattachFrontMatter(parts.FrontMatter, RenderNormalized(document, pipeline));
        IReadOnlyList<string> warnings = BuildReflowWarnings(rawMarkdown, changes);

        return new MarkdownReflowResult(
            markdown,
            changes.Count(change => change.OriginalLevel != change.NormalizedLevel),
            warnings);
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

    private static IReadOnlyList<HeadingChange> NormalizeHeadingHierarchy(
        MarkdownDocument document,
        int topHeadingLevel)
    {
        var hierarchy = new Stack<(int OriginalLevel, int NormalizedLevel)>();
        var changes = new List<HeadingChange>();

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            heading.IsSetext = false;
            heading.HeaderChar = '#';

            int requestedLevel = Math.Clamp(heading.Level, 1, 6);

            while (hierarchy.Count > 0 && hierarchy.Peek().OriginalLevel >= requestedLevel)
            {
                hierarchy.Pop();
            }

            int unclampedLevel = hierarchy.Count == 0
                ? topHeadingLevel
                : hierarchy.Peek().NormalizedLevel + 1;
            int normalizedLevel = Math.Min(unclampedLevel, 6);

            heading.Level = normalizedLevel;
            heading.HeaderCharCount = normalizedLevel;

            hierarchy.Push((requestedLevel, normalizedLevel));
            changes.Add(new HeadingChange(
                requestedLevel,
                normalizedLevel,
                heading.Line + 1,
                unclampedLevel > 6));
        }

        return changes;
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

    private static IReadOnlyList<string> BuildReflowWarnings(
        string rawMarkdown,
        IReadOnlyList<HeadingChange> changes)
    {
        var warnings = new List<string>();
        int changed = changes.Count(change => change.OriginalLevel != change.NormalizedLevel);

        if (changes.Count == 0)
        {
            warnings.Add("No Markdown headings were found.");
        }
        else if (changed > 0)
        {
            warnings.Add("Review hand-written tables of contents, outline prose, and links that describe heading levels.");
        }

        if (changes.Any(change => change.WasClamped))
        {
            warnings.Add("Some deep child headings reached H6 and could not be nested further.");
        }

        if (Regex.IsMatch(rawMarkdown, @"<h[1-6]\b", RegexOptions.IgnoreCase))
        {
            warnings.Add("Raw HTML heading tags were detected; reflow does not rewrite HTML headings.");
        }

        if (Regex.IsMatch(rawMarkdown, @"\]\(\s*#|href\s*=\s*[""']#", RegexOptions.IgnoreCase))
        {
            warnings.Add("Anchor links were detected; check manually maintained navigation after reflow.");
        }

        return warnings;
    }

    private sealed record HeadingChange(
        int OriginalLevel,
        int NormalizedLevel,
        int LineNumber,
        bool WasClamped);

    private sealed record MarkdownParts(string? FrontMatter, string Body);
}
