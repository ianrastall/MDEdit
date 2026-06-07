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
    private readonly IUnsavedChangesPromptService _unsavedChangesPrompt;

    [ObservableProperty]
    private ObservableCollection<DocumentViewModel> _tabs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTab))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseAllSavedCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePreviewCommand))]
    private DocumentViewModel? _activeTab;

    public ShellViewModel(
        IServiceProvider services,
        IFilePickerService filePicker,
        IApplicationService application,
        IUnsavedChangesPromptService unsavedChangesPrompt)
    {
        _services = services;
        _filePicker = filePicker;
        _application = application;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        NewTab();
    }

    public bool HasActiveTab => ActiveTab is not null;
    public bool HasUnsavedChanges => Tabs.Any(tab => tab.IsDirty);

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
            SetStatusMessage("Open failed: MDEdit opens Markdown and plain-text files only.");
            return;
        }

        string markdown;
        try
        {
            markdown = await File.ReadAllTextAsync(filePath, MarkdownEncoding);
        }
        catch (Exception ex)
        {
            SetStatusMessage($"Open failed: {ex.Message}");
            return;
        }

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

        try
        {
            await File.WriteAllTextAsync(ActiveTab.Document.FilePath, ActiveTab.RawMarkdown, MarkdownEncoding);
            ActiveTab.MarkSaved(ActiveTab.Document.FilePath);
        }
        catch (Exception ex)
        {
            ActiveTab.StatusMessage = $"Save failed: {ex.Message}";
        }
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

        try
        {
            await File.WriteAllTextAsync(filePath, ActiveTab.RawMarkdown, MarkdownEncoding);
            ActiveTab.MarkSaved(filePath);
        }
        catch (Exception ex)
        {
            ActiveTab.StatusMessage = $"Save failed: {ex.Message}";
        }
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
    public void CloseAllSaved()
    {
        DocumentViewModel[] savedTabs = Tabs.Where(tab => !tab.IsDirty).ToArray();
        int closedCount = savedTabs.Length;

        foreach (DocumentViewModel tab in savedTabs)
        {
            Tabs.Remove(tab);
        }

        if (Tabs.Count == 0)
        {
            NewTab();
        }
        else if (ActiveTab is null || !Tabs.Contains(ActiveTab))
        {
            ActiveTab = Tabs[0];
        }

        int unsavedCount = Tabs.Count(tab => tab.IsDirty);
        ActiveTab!.StatusMessage = closedCount == 0
            ? "No saved tabs to close."
            : unsavedCount == 0
                ? $"Closed {closedCount:N0} saved tab(s)."
                : $"Closed {closedCount:N0} saved tab(s); {unsavedCount:N0} unsaved tab(s) remain.";
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
    private async Task ExitAsync()
    {
        if (!await ConfirmCloseAsync())
        {
            return;
        }

        _application.Exit();
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        int unsavedCount = Tabs.Count(tab => tab.IsDirty);
        if (unsavedCount == 0)
        {
            return true;
        }

        bool shouldClose = await _unsavedChangesPrompt.ConfirmDiscardUnsavedChangesAsync(unsavedCount);
        if (!shouldClose)
        {
            SetStatusMessage("Exit canceled; unsaved changes remain.");
        }

        return shouldClose;
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

    private void SetStatusMessage(string message)
    {
        if (ActiveTab is not null)
        {
            ActiveTab.StatusMessage = message;
        }
    }
}
