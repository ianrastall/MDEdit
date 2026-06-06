using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MDEdit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MDEdit.Application.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private static readonly Encoding MarkdownEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
        ".mdown",
        ".mkdn",
        ".txt",
    };

    private readonly IServiceProvider _services;
    private readonly IFilePickerService _filePicker;
    private readonly IApplicationService _application;

    [ObservableProperty]
    private ObservableCollection<DocumentViewModel> _tabs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTab))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    private DocumentViewModel? _activeTab;

    public ShellViewModel(
        IServiceProvider services,
        IFilePickerService filePicker,
        IApplicationService application)
    {
        _services = services;
        _filePicker = filePicker;
        _application = application;
        NewTab();
    }

    public bool HasActiveTab => ActiveTab is not null;

    [RelayCommand]
    public void NewTab()
    {
        var tab = _services.GetRequiredService<DocumentViewModel>();
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        string? filePath = await _filePicker.PickOpenMarkdownFileAsync();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await OpenFileAsync(filePath);
    }

    public async Task OpenFileAsync(string filePath)
    {
        if (!MarkdownExtensions.Contains(Path.GetExtension(filePath)))
        {
            throw new NotSupportedException("MDEdit opens Markdown and plain-text files only.");
        }

        string markdown = await File.ReadAllTextAsync(filePath, MarkdownEncoding);
        DocumentViewModel tab = ReusableBlankTab() ?? _services.GetRequiredService<DocumentViewModel>();

        if (!Tabs.Contains(tab))
        {
            Tabs.Add(tab);
        }

        ActiveTab = tab;
        tab.Load(filePath, markdown);
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private async Task SaveAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ActiveTab.Document.FilePath))
        {
            await SaveAsAsync();
            return;
        }

        await File.WriteAllTextAsync(ActiveTab.Document.FilePath, ActiveTab.RawMarkdown, MarkdownEncoding);
        ActiveTab.MarkSaved(ActiveTab.Document.FilePath);
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private async Task SaveAsAsync()
    {
        if (ActiveTab is null)
        {
            return;
        }

        string suggestedName = ActiveTab.Document.DocumentTitle == "Untitled"
            ? "Untitled.md"
            : ActiveTab.Document.DocumentTitle;

        string? filePath = await _filePicker.PickSaveMarkdownFileAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await File.WriteAllTextAsync(filePath, ActiveTab.RawMarkdown, MarkdownEncoding);
        ActiveTab.MarkSaved(filePath);
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    public void CloseTab(DocumentViewModel? tab = null)
    {
        DocumentViewModel? target = tab ?? ActiveTab;
        if (target is null)
        {
            return;
        }

        int index = Tabs.IndexOf(target);
        Tabs.Remove(target);

        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }

        ActiveTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void Format()
    {
        ActiveTab?.FormatCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void TogglePreview()
    {
        ActiveTab?.TogglePreviewCommand.Execute(null);
    }

    [RelayCommand]
    private void Exit()
    {
        _application.Exit();
    }

    private DocumentViewModel? ReusableBlankTab()
    {
        if (ActiveTab is null)
        {
            return null;
        }

        return !ActiveTab.IsDirty
            && string.IsNullOrEmpty(ActiveTab.RawMarkdown)
            && string.IsNullOrWhiteSpace(ActiveTab.Document.FilePath)
                ? ActiveTab
                : null;
    }
}
