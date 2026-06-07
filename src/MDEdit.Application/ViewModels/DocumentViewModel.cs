using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MDEdit.Core.Interfaces;
using MDEdit.Core.Models;
using MDEdit.Core.Utilities;

namespace MDEdit.Application.ViewModels;

public partial class DocumentViewModel : ObservableObject
{
    private const int HeadingParseDebounceMilliseconds = 400;
    private readonly IMarkdownFormattingService _formatter;
    private bool _normalizingRawMarkdown;
    private bool _suppressRawMarkdownChangedSideEffects;
    private CancellationTokenSource? _headingParseDebounceCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    private DocumentContext _document = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewMarkdown))]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    [NotifyPropertyChangedFor(nameof(LineCount))]
    [NotifyPropertyChangedFor(nameof(DocumentStatisticsText))]
    private string _rawMarkdown = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlavorDisplayName))]
    [NotifyPropertyChangedFor(nameof(SelectedFlavorOption))]
    private MarkdownFlavor _flavor = MarkdownFlavor.GitHubFlavored;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsPreviewVisible))]
    private bool _isPreviewMode;

    [ObservableProperty]
    private bool _isOutlineVisible;

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
    private string _sourceLineEndingDisplayName = "N/A";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineEndingStatusText))]
    [NotifyPropertyChangedFor(nameof(DocumentStatisticsText))]
    private string _lineEndingDisplayName = "N/A";

    public DocumentViewModel(IMarkdownFormattingService formatter)
    {
        _formatter = formatter;
        ParseHeadings(RawMarkdown);
    }

    public IReadOnlyList<MarkdownFlavor> AvailableFlavors => MarkdownFlavorCatalog.All;
    public IReadOnlyList<MarkdownFlavorOption> AvailableFlavorOptions => MarkdownFlavorCatalog.Options;
    public MarkdownFlavorOption SelectedFlavorOption
    {
        get => MarkdownFlavorCatalog.OptionFor(Flavor);
        set
        {
            if (value is not null)
            {
                Flavor = value.Flavor;
            }
        }
    }

    public string DocumentTitle => IsDirty ? "* " + Document.DocumentTitle : Document.DocumentTitle;
    public string PreviewMarkdown => RawMarkdown;
    public string FlavorDisplayName => MarkdownFlavorCatalog.DisplayName(Flavor);
    public string EncodingDisplayName => "UTF-8";
    public string LineEndingStatusText => SourceLineEndingDisplayName == LineEndingDisplayName
        ? LineEndingDisplayName
        : $"{SourceLineEndingDisplayName} -> {LineEndingDisplayName}";
    public bool IsEditorVisible => !IsPreviewMode;
    public bool IsPreviewVisible => IsPreviewMode;
    public int CharacterCount => RawMarkdown.Length;
    public int LineCount => MarkdownTextUtilities.CountLines(RawMarkdown);
    public string DocumentStatisticsText =>
        $"{LineCount:N0} lines  |  {CharacterCount:N0} chars  |  {EncodingDisplayName}  |  EOL {LineEndingStatusText}  |  {FlavorDisplayName}";

    public void Load(string filePath, string markdown)
    {
        string sourceLineEnding = DetectLineEndingDisplayName(markdown);
        string normalizedMarkdown = MarkdownTextUtilities.NormalizeLineEndingsToCrlf(markdown);
        bool normalizedLineEndings = normalizedMarkdown != markdown;

        SourceLineEndingDisplayName = sourceLineEnding;
        LineEndingDisplayName = DetectLineEndingDisplayName(normalizedMarkdown);
        SetRawMarkdownWithoutChangeTracking(normalizedMarkdown);
        SetDocument(filePath, normalizedMarkdown, Flavor);
        IsDirty = normalizedLineEndings;
        StatusMessage = normalizedLineEndings
            ? $"Opened {Path.GetFileName(filePath)}; normalized EOL {sourceLineEnding} to CRLF."
            : $"Opened {Path.GetFileName(filePath)}.";
        RefreshDocumentState();
    }

    public void MarkSaved(string filePath)
    {
        string normalizedMarkdown = MarkdownTextUtilities.NormalizeLineEndingsToCrlf(RawMarkdown);

        if (RawMarkdown != normalizedMarkdown)
        {
            SetRawMarkdownWithoutChangeTracking(normalizedMarkdown);
            LineEndingDisplayName = DetectLineEndingDisplayName(normalizedMarkdown);
        }

        SourceLineEndingDisplayName = LineEndingDisplayName;
        IsDirty = false;
        SetDocument(filePath, normalizedMarkdown, Flavor);
        StatusMessage = $"Saved {Path.GetFileName(filePath)}.";
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
            : $"{existingMarkdown}{MarkdownTextUtilities.Crlf}{MarkdownTextUtilities.Crlf}{content}";
        StatusMessage = "Content appended.";
    }

    public void ReflowToTopHeadingLevel(int topHeadingLevel)
    {
        ReflowTopHeadingLevel = HeadingLevelOption.FromLevel(topHeadingLevel);
        ReflowCommand.Execute(null);
    }

    public void ToggleOutlineVisibility()
    {
        IsOutlineVisible = !IsOutlineVisible;
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
            string normalized = MarkdownTextUtilities.NormalizeLineEndingsToCrlf(value);
            if (normalized != value)
            {
                _normalizingRawMarkdown = true;
                try
                {
                    RawMarkdown = normalized;
                }
                finally
                {
                    _normalizingRawMarkdown = false;
                }
                return;
            }
        }

        if (_suppressRawMarkdownChangedSideEffects)
        {
            return;
        }

        LineEndingDisplayName = DetectLineEndingDisplayName(value);
        SetDocument(Document.FilePath, value, Flavor);
        IsDirty = true;
        QueueHeadingParse(value);
    }

    partial void OnFlavorChanged(MarkdownFlavor value)
    {
        SetDocument(Document.FilePath, RawMarkdown, value);
        OnPropertyChanged(nameof(DocumentStatisticsText));
    }

    private void RefreshDocumentState()
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(PreviewMarkdown));
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

    private void SetRawMarkdownWithoutChangeTracking(string markdown)
    {
        _suppressRawMarkdownChangedSideEffects = true;
        try
        {
            RawMarkdown = markdown;
        }
        finally
        {
            _suppressRawMarkdownChangedSideEffects = false;
        }
    }

    private void SetDocument(string? filePath, string rawMarkdown, MarkdownFlavor flavor)
    {
        if (Document.FilePath == filePath
            && Document.RawMarkdown == rawMarkdown
            && Document.Flavor == flavor)
        {
            return;
        }

        Document = new DocumentContext
        {
            FilePath = filePath,
            RawMarkdown = rawMarkdown,
            Flavor = flavor,
        };
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
        int lineNumber = 1;
        int characterOffset = 0;

        foreach (ReadOnlySpan<char> lineSpan in markdown.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> trimmedLine = lineSpan.TrimStart();

            if (TryUpdateFence(trimmedLine, ref fence) || fence is not null)
            {
                characterOffset = NextLineOffset(markdown, characterOffset, lineSpan.Length);
                lineNumber++;
                continue;
            }

            if (trimmedLine.IsEmpty || trimmedLine[0] != '#')
            {
                characterOffset = NextLineOffset(markdown, characterOffset, lineSpan.Length);
                lineNumber++;
                continue;
            }

            string line = lineSpan.ToString();
            Match match = HeadingRegex().Match(line);
            if (!match.Success)
            {
                characterOffset = NextLineOffset(markdown, characterOffset, lineSpan.Length);
                lineNumber++;
                continue;
            }

            int level = match.Groups[1].Value.Length;
            string title = Regex.Replace(match.Groups[2].Value.Trim(), @"[ \t]+#+[ \t]*$", "").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                characterOffset = NextLineOffset(markdown, characterOffset, lineSpan.Length);
                lineNumber++;
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
            characterOffset = NextLineOffset(markdown, characterOffset, lineSpan.Length);
            lineNumber++;
        }

        HeadingNodes = roots;
    }

    private static int NextLineOffset(string text, int lineStart, int lineLength)
    {
        int separatorStart = lineStart + lineLength;
        if (separatorStart >= text.Length)
        {
            return separatorStart;
        }

        if (text[separatorStart] == '\r')
        {
            if (separatorStart + 1 < text.Length && text[separatorStart + 1] == '\n')
            {
                return separatorStart + 2;
            }

            return separatorStart + 1;
        }

        return text[separatorStart] == '\n'
            ? separatorStart + 1
            : separatorStart;
    }

    private static bool TryUpdateFence(ReadOnlySpan<char> trimmedLine, ref string? fence)
    {
        if (fence is null)
        {
            int backtickFenceLength = CountFenceCharacters(trimmedLine, '`');
            if (backtickFenceLength >= 3)
            {
                fence = new string('`', backtickFenceLength);
                return true;
            }

            int tildeFenceLength = CountFenceCharacters(trimmedLine, '~');
            if (tildeFenceLength >= 3)
            {
                fence = new string('~', tildeFenceLength);
                return true;
            }

            return false;
        }

        char fenceCharacter = fence[0];
        int fenceLength = CountFenceCharacters(trimmedLine, fenceCharacter);
        if (fenceLength >= fence.Length)
        {
            ReadOnlySpan<char> rest = trimmedLine[fenceLength..];
            if (rest.IsEmpty || rest.IsWhiteSpace())
            {
                fence = null;
                return true;
            }
        }

        return false;
    }

    private static int CountFenceCharacters(ReadOnlySpan<char> line, char fenceCharacter)
    {
        int count = 0;
        while (count < line.Length && line[count] == fenceCharacter)
        {
            count++;
        }

        return count;
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

        if (crlfCount == 0 && lfCount == 0 && crCount == 0)
        {
            return "N/A";
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
        int originalLines = MarkdownTextUtilities.CountLines(original);
        int formattedLines = MarkdownTextUtilities.CountLines(formatted);
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

    [GeneratedRegex(@"^\s{0,3}(#{1,6})(?:[ \t]+|$)(.*)$")]
    private static partial Regex HeadingRegex();
}
