using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MDEdit.Core.Interfaces;
using MDEdit.Core.Models;

namespace MDEdit.Application.ViewModels;

public partial class DocumentViewModel : ObservableObject
{
    private readonly IMarkdownFormattingService _formatter;

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

    public DocumentViewModel(IMarkdownFormattingService formatter)
    {
        _formatter = formatter;
    }

    public IReadOnlyList<MarkdownFlavor> AvailableFlavors => MarkdownFlavorCatalog.All;
    public string DocumentTitle => IsDirty ? "* " + Document.DocumentTitle : Document.DocumentTitle;
    public string RenderedMarkdown => RawMarkdown;
    public string FlavorDisplayName => MarkdownFlavorCatalog.DisplayName(Flavor);
    public bool IsEditorVisible => !IsPreviewMode;
    public bool IsPreviewVisible => IsPreviewMode;
    public int CharacterCount => RawMarkdown.Length;
    public int LineCount => CountLines(RawMarkdown);
    public string DocumentStatisticsText =>
        $"{LineCount:N0} lines  |  {CharacterCount:N0} chars  |  {FlavorDisplayName}";

    public void Load(string filePath, string markdown)
    {
        Document = new DocumentContext
        {
            FilePath = filePath,
            RawMarkdown = markdown,
            Flavor = Flavor,
        };

        RawMarkdown = markdown;
        IsDirty = false;
        StatusMessage = $"Opened {Path.GetFileName(filePath)}.";
        RefreshDocumentState();
    }

    public void MarkSaved(string filePath)
    {
        Document = new DocumentContext
        {
            FilePath = filePath,
            RawMarkdown = RawMarkdown,
            Flavor = Flavor,
        };

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

    [RelayCommand]
    private void Format()
    {
        string formatted = _formatter.Format(RawMarkdown, Flavor);

        if (LooksSuspiciouslyCollapsed(RawMarkdown, formatted))
        {
            StatusMessage = "Format skipped: output looked collapsed.";
            return;
        }

        RawMarkdown = formatted;
        StatusMessage = "Formatted Markdown.";
    }

    [RelayCommand]
    private void TogglePreview()
    {
        IsPreviewMode = !IsPreviewMode;
        StatusMessage = IsPreviewMode ? "Preview mode." : "Editor mode.";
    }

    partial void OnRawMarkdownChanged(string value)
    {
        Document = new DocumentContext
        {
            FilePath = Document.FilePath,
            RawMarkdown = value,
            Flavor = Flavor,
        };

        IsDirty = true;
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
        OnPropertyChanged(nameof(DocumentStatisticsText));
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
}
