using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MDEdit.Core.Interfaces;
using MDEdit.Core.Models;

namespace MDEdit.Application.ViewModels;

public partial class DocumentViewModel : ObservableObject
{
    private const string Crlf = "\r\n";
    private const int HeadingParseDebounceMilliseconds = 400;
    private readonly IMarkdownFormattingService _formatter;
    private bool _normalizingRawMarkdown;
    private CancellationTokenSource? _headingParseDebounceCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    private DocumentContext _document = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenderedMarkdown))]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    [NotifyPropertyChangedFor(nameof(LineCount))]
    [NotifyPropertyChangedFor(nameof(DocumentStatisticsText))]
    private string _rawMarkdown = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlavorDisplayName))]
    private MarkdownFlavor _flavor = MarkdownFlavor.GitHubFlavored;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    private bool _isPreviewMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    private bool _isDirty;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private ObservableCollection<HeadingNode> _headingNodes = [];

    [ObservableProperty]
    private HeadingLevelOption _reflowTopHeadingLevel = HeadingLevelOption.H1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineEndingStatusText))]
    [NotifyPropertyChangedFor(nameof(DocumentStatisticsText))]
    private string _sourceLineEndingDisplayName = "CRLF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineEndingStatusText))]
    [NotifyPropertyChangedFor(nameof(DocumentStatisticsText))]
    private string _lineEndingDisplayName = "CRLF";

    public DocumentViewModel(IMarkdownFormattingService formatter)
    {
        _formatter = formatter;
        ParseHeadings(RawMarkdown);
    }

    public IReadOnlyList<MarkdownFlavor> AvailableFlavors => MarkdownFlavorCatalog.All;
    public string DocumentTitle => IsDirty ? "* " + Document.DocumentTitle : Document.DocumentTitle;
    public string RenderedMarkdown => RawMarkdown;
    public string FlavorDisplayName => MarkdownFlavorCatalog.DisplayName(Flavor);
    public string EncodingDisplayName => "UTF-8";
    public string LineEndingStatusText => SourceLineEndingDisplayName == LineEndingDisplayName
        ? LineEndingDisplayName
        : $"{SourceLineEndingDisplayName} -> {LineEndingDisplayName}";
    public bool IsEditorVisible => !IsPreviewMode;
    public bool IsPreviewVisible => IsPreviewMode;
    public int CharacterCount => RawMarkdown.Length;
    public int LineCount => CountLines(RawMarkdown);
    public string DocumentStatisticsText =>
        $"{LineCount:N0} lines  |  {CharacterCount:N0} chars  |  {EncodingDisplayName}  |  EOL {LineEndingStatusText}  |  {FlavorDisplayName}";

    public void Load(string filePath, string markdown)
    {
        string sourceLineEnding = DetectLineEndingDisplayName(markdown);
        string normalizedMarkdown = NormalizeLineEndingsToCrlf(markdown);
        bool normalizedLineEndings = normalizedMarkdown != markdown;

        SourceLineEndingDisplayName = sourceLineEnding;
        LineEndingDisplayName = DetectLineEndingDisplayName(normalizedMarkdown);

        Document = new DocumentContext
        {
            FilePath = filePath,
            RawMarkdown = normalizedMarkdown,
            Flavor = Flavor,
        };

        RawMarkdown = normalizedMarkdown;
        IsDirty = normalizedLineEndings;
        StatusMessage = normalizedLineEndings
            ? $"Opened {Path.GetFileName(filePath)}; normalized EOL {sourceLineEnding} to CRLF."
            : $"Opened {Path.GetFileName(filePath)}.";
        RefreshDocumentState();
    }

    public void MarkSaved(string filePath)
    {
        string normalizedMarkdown = NormalizeLineEndingsToCrlf(RawMarkdown);

        if (RawMarkdown != normalizedMarkdown)
        {
            RawMarkdown = normalizedMarkdown;
        }

        Document = new DocumentContext
        {
            FilePath = filePath,
            RawMarkdown = normalizedMarkdown,
            Flavor = Flavor,
        };

        SourceLineEndingDisplayName = LineEndingDisplayName;
        IsDirty = false;
        StatusMessage = $"Saved {Path.GetFileName(filePath)}.";
        OnPropertyChanged(nameof(DocumentTitle));
    }

    public void SetMarkdownFromEditor(string markdown)
    {
        if (RawMarkdown == markdown)
        {
            return;
        }

        RawMarkdown = markdown;
    }

    public void AppendToEnd(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        string existingMarkdown = RawMarkdown.TrimEnd();
        RawMarkdown = string.IsNullOrEmpty(existingMarkdown)
            ? content
            : $"{existingMarkdown}{Crlf}{Crlf}{content}";
        StatusMessage = "Content appended.";
    }

    public void ReflowToTopHeadingLevel(int topHeadingLevel)
    {
        ReflowTopHeadingLevel = HeadingLevelOption.FromLevel(topHeadingLevel);
        ReflowCommand.Execute(null);
    }

    [RelayCommand]
    private async Task FormatAsync()
    {
        string raw = RawMarkdown;
        MarkdownFlavor flavor = Flavor;
        string formatted;

        try
        {
            formatted = await Task.Run(() => _formatter.Format(raw, flavor));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Format failed: {ex.Message}";
            return;
        }

        if (LooksSuspiciouslyCollapsed(raw, formatted))
        {
            StatusMessage = "Format skipped: output looked collapsed.";
            return;
        }

        RawMarkdown = formatted;
        StatusMessage = "Formatted Markdown.";
    }

    [RelayCommand]
    private async Task ReflowAsync()
    {
        string raw = RawMarkdown;
        MarkdownFlavor flavor = Flavor;
        int topHeadingLevel = ReflowTopHeadingLevel.Level;
        MarkdownReflowResult result;

        try
        {
            result = await Task.Run(() => _formatter.ReflowHeadings(raw, flavor, topHeadingLevel));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reflow failed: {ex.Message}";
            return;
        }

        if (LooksSuspiciouslyCollapsed(raw, result.Markdown))
        {
            StatusMessage = "Reflow skipped: output looked collapsed.";
            return;
        }

        RawMarkdown = result.Markdown;
        StatusMessage = BuildReflowStatus(result);
    }

    [RelayCommand]
    private void TogglePreview()
    {
        IsPreviewMode = !IsPreviewMode;
        StatusMessage = IsPreviewMode ? "Preview mode." : "Editor mode.";
    }

    partial void OnRawMarkdownChanged(string value)
    {
        if (!_normalizingRawMarkdown)
        {
            string normalized = NormalizeLineEndingsToCrlf(value);
            if (normalized != value)
            {
                _normalizingRawMarkdown = true;
                RawMarkdown = normalized;
                _normalizingRawMarkdown = false;
                return;
            }
        }

        LineEndingDisplayName = DetectLineEndingDisplayName(value);
        Document = new DocumentContext
        {
            FilePath = Document.FilePath,
            RawMarkdown = value,
            Flavor = Flavor,
        };

        IsDirty = true;
        QueueHeadingParse(value);
    }

    partial void OnFlavorChanged(MarkdownFlavor value)
    {
        Document = new DocumentContext
        {
            FilePath = Document.FilePath,
            RawMarkdown = RawMarkdown,
            Flavor = value,
        };

        OnPropertyChanged(nameof(DocumentStatisticsText));
    }

    private void RefreshDocumentState()
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(RenderedMarkdown));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(LineEndingStatusText));
        OnPropertyChanged(nameof(DocumentStatisticsText));
        ParseHeadingsImmediately(RawMarkdown);
    }

    private void QueueHeadingParse(string markdown)
    {
        _headingParseDebounceCts?.Cancel();

        var cts = new CancellationTokenSource();
        _headingParseDebounceCts = cts;
        _ = DebounceParseHeadingsAsync(markdown, cts);
    }

    private async Task DebounceParseHeadingsAsync(string markdown, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(HeadingParseDebounceMilliseconds, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            ParseHeadings(markdown);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_headingParseDebounceCts, cts))
            {
                _headingParseDebounceCts = null;
            }

            cts.Dispose();
        }
    }

    private void ParseHeadingsImmediately(string markdown)
    {
        _headingParseDebounceCts?.Cancel();
        ParseHeadings(markdown);
    }

    private string BuildReflowStatus(MarkdownReflowResult result)
    {
        string summary = result.ChangedHeadingCount == 0
            ? $"Reflow made no heading-level changes; top level {ReflowTopHeadingLevel.DisplayName}."
            : $"Reflow changed {result.ChangedHeadingCount:N0} heading level(s); top level {ReflowTopHeadingLevel.DisplayName}.";

        return result.Warnings.Count == 0
            ? summary
            : $"{summary} {result.Warnings[0]}";
    }

    private void ParseHeadings(string markdown)
    {
        var roots = new ObservableCollection<HeadingNode>();
        var stack = new Stack<HeadingNode>();
        string? fence = null;

        foreach ((string line, int lineNumber, int characterOffset) in EnumerateLines(markdown))
        {
            if (TryUpdateFence(line, ref fence) || fence is not null)
            {
                continue;
            }

            Match match = HeadingRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            int level = match.Groups[1].Value.Length;
            string title = Regex.Replace(match.Groups[2].Value.Trim(), @"[ \t]+#+[ \t]*$", "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var node = new HeadingNode
            {
                Title = title,
                Level = level,
                LineNumber = lineNumber,
                CharacterOffset = characterOffset,
            };

            while (stack.Count > 0 && stack.Peek().Level >= level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack.Peek().Children.Add(node);
            }

            stack.Push(node);
        }

        HeadingNodes = roots;
    }

    private static IEnumerable<(string Line, int LineNumber, int CharacterOffset)> EnumerateLines(string text)
    {
        int lineNumber = 1;
        int lineStart = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\r' && text[i] != '\n')
            {
                continue;
            }

            yield return (text[lineStart..i], lineNumber, lineStart);

            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            lineNumber++;
            lineStart = i + 1;
        }

        yield return (text[lineStart..], lineNumber, lineStart);
    }

    private static bool TryUpdateFence(string line, ref string? fence)
    {
        string trimmed = line.TrimStart();
        if (fence is null)
        {
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                fence = "```";
                return true;
            }

            if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = "~~~";
                return true;
            }

            return false;
        }

        if (trimmed.StartsWith(fence, StringComparison.Ordinal))
        {
            fence = null;
            return true;
        }

        return false;
    }

    private static string NormalizeLineEndingsToCrlf(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Crlf, StringComparison.Ordinal);
    }

    private static string DetectLineEndingDisplayName(string text)
    {
        int crlfCount = 0;
        int lfCount = 0;
        int crCount = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlfCount++;
                    i++;
                }
                else
                {
                    crCount++;
                }
            }
            else if (text[i] == '\n')
            {
                lfCount++;
            }
        }

        int kinds = 0;
        if (crlfCount > 0)
        {
            kinds++;
        }

        if (lfCount > 0)
        {
            kinds++;
        }

        if (crCount > 0)
        {
            kinds++;
        }

        if (kinds > 1)
        {
            return "Mixed";
        }

        if (lfCount > 0)
        {
            return "LF";
        }

        if (crCount > 0)
        {
            return "CR";
        }

        return "CRLF";
    }

    private static bool LooksSuspiciouslyCollapsed(string original, string formatted)
    {
        int originalLines = CountLines(original);
        int formattedLines = CountLines(formatted);
        if (originalLines < 10 || formattedLines > Math.Max(3, originalLines / 4))
        {
            return false;
        }

        return LongestLineLength(formatted) >= 500;
    }

    private static int LongestLineLength(string text)
    {
        int longest = 0;
        int current = 0;

        foreach (char ch in text)
        {
            if (ch is '\r' or '\n')
            {
                longest = Math.Max(longest, current);
                current = 0;
                continue;
            }

            current++;
        }

        return Math.Max(longest, current);
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                count++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    [GeneratedRegex(@"^\s{0,3}(#{1,6})(?:[ \t]+|$)(.*)$")]
    private static partial Regex HeadingRegex();
}
