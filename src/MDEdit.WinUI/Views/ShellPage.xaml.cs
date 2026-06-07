using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using MDEdit.Application.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MDEdit.WinUI.Views;

public sealed partial class ShellPage : Page
{
    private readonly Dictionary<DocumentViewModel, TabViewItem> _tabItems = [];
    private bool _isInsertDialogOpen;
    private bool _syncingSelection;

    public ShellViewModel ViewModel { get; }

    public ShellPage()
    {
        ViewModel = ((App)Microsoft.UI.Xaml.Application.Current).Host.Services.GetRequiredService<ShellViewModel>();
        this.InitializeComponent();

        ViewModel.Tabs.CollectionChanged += Tabs_CollectionChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += OnUnloaded;

        SyncTabs();
        SelectActiveTab();
        RefreshStructureCommandState();
    }

    public Task OpenExternalFileAsync(string filePath)
    {
        return ViewModel.OpenFileAsync(filePath);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Tabs.CollectionChanged -= Tabs_CollectionChanged;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Unloaded -= OnUnloaded;

        foreach (DocumentViewModel viewModel in _tabItems.Keys.ToArray())
        {
            viewModel.PropertyChanged -= TabViewModel_PropertyChanged;
        }
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            SyncTabs();
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (DocumentViewModel viewModel in e.OldItems.OfType<DocumentViewModel>())
            {
                RemoveTab(viewModel);
            }
        }

        if (e.NewItems is not null)
        {
            int index = e.NewStartingIndex;
            foreach (DocumentViewModel viewModel in e.NewItems.OfType<DocumentViewModel>())
            {
                AddTab(viewModel, index++);
            }
        }

        SelectActiveTab();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ActiveTab))
        {
            SelectActiveTab();
            RefreshStructureCommandState();
        }
    }

    private void TabViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(DocumentViewModel.DocumentTitle)
            or nameof(DocumentViewModel.Document)
            or nameof(DocumentViewModel.IsDirty))
        {
            RefreshTabHeader(viewModel);
        }
    }

    private void SyncTabs()
    {
        foreach (DocumentViewModel viewModel in _tabItems.Keys.ToArray())
        {
            RemoveTabSubscriptions(viewModel);
        }

        _tabItems.Clear();
        DocumentTabs.TabItems.Clear();

        for (int i = 0; i < ViewModel.Tabs.Count; i++)
        {
            AddTab(ViewModel.Tabs[i], i);
        }
    }

    private void AddTab(DocumentViewModel viewModel, int index)
    {
        if (_tabItems.ContainsKey(viewModel))
        {
            return;
        }

        var editorPage = new EditorPage(viewModel);
        editorPage.HeadingCommandStateChanged += EditorPage_HeadingCommandStateChanged;

        var tab = new TabViewItem
        {
            Header = viewModel.DocumentTitle,
            Tag = viewModel,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = editorPage,
        };

        _tabItems[viewModel] = tab;
        viewModel.PropertyChanged += TabViewModel_PropertyChanged;

        if (index < 0 || index > DocumentTabs.TabItems.Count)
        {
            DocumentTabs.TabItems.Add(tab);
        }
        else
        {
            DocumentTabs.TabItems.Insert(index, tab);
        }
    }

    private void RemoveTab(DocumentViewModel viewModel)
    {
        if (!_tabItems.TryGetValue(viewModel, out TabViewItem? tab))
        {
            return;
        }

        RemoveTabSubscriptions(viewModel);
        DocumentTabs.TabItems.Remove(tab);
        _tabItems.Remove(viewModel);
        RefreshStructureCommandState();
    }

    private void RefreshTabHeader(DocumentViewModel viewModel)
    {
        if (_tabItems.TryGetValue(viewModel, out TabViewItem? tab))
        {
            tab.Header = viewModel.DocumentTitle;
        }
    }

    private void SelectActiveTab()
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        DocumentTabs.SelectedItem = ViewModel.ActiveTab is not null
            && _tabItems.TryGetValue(ViewModel.ActiveTab, out TabViewItem? tab)
                ? tab
                : null;
        _syncingSelection = false;
        RefreshStructureCommandState();
    }

    private void DocumentTabs_AddTabButtonClick(TabView sender, object args)
    {
        ViewModel.NewTabCommand.Execute(null);
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (DocumentTabs.SelectedItem is TabViewItem { Tag: DocumentViewModel viewModel })
        {
            ViewModel.ActiveTab = viewModel;
            RefreshStructureCommandState();
        }
    }

    private void DocumentTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        DocumentViewModel? viewModel = args.Tab?.Tag as DocumentViewModel
            ?? (args.Item as TabViewItem)?.Tag as DocumentViewModel
            ?? args.Item as DocumentViewModel;

        if (viewModel is not null)
        {
            ViewModel.CloseTabCommand.Execute(viewModel);
        }
    }

    private EditorPage? ActiveEditorPage()
    {
        return DocumentTabs.SelectedItem is TabViewItem { Content: EditorPage editorPage }
            ? editorPage
            : null;
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab is null)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(ViewModel.ActiveTab.RawMarkdown);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        ViewModel.ActiveTab.StatusMessage = "Copied all Markdown.";
    }

    private async void AppendToEnd_Click(object sender, RoutedEventArgs e)
    {
        string? content = await PromptForInsertContentAsync("Append to End");
        if (string.IsNullOrEmpty(content) || ViewModel.ActiveTab is null)
        {
            return;
        }

        ViewModel.ActiveTab.AppendToEnd(content);
    }

    private async void AppendAtCursor_Click(object sender, RoutedEventArgs e)
    {
        string? content = await PromptForInsertContentAsync("Append at Cursor");
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        ActiveEditorPage()?.InsertAtCursor(content);
    }

    private void HeadingOutline_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.ToggleOutlinePane();
    }

    private void NumberHeadings_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.NumberHeadings();
        RefreshStructureCommandState();
    }

    private void RemoveHeadingNumbers_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.RemoveHeadingNumbers();
        RefreshStructureCommandState();
    }

    private void PromoteHeading_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.PromoteCurrentHeading();
        RefreshStructureCommandState();
    }

    private void DemoteHeading_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.DemoteCurrentHeading();
        RefreshStructureCommandState();
    }

    private void PromoteSubtree_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.PromoteCurrentSubtree();
        RefreshStructureCommandState();
    }

    private void DemoteSubtree_Click(object sender, RoutedEventArgs e)
    {
        ActiveEditorPage()?.DemoteCurrentSubtree();
        RefreshStructureCommandState();
    }

    private void EditorPage_HeadingCommandStateChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActiveEditorPage()))
        {
            RefreshStructureCommandState();
        }
    }

    private void RefreshStructureCommandState()
    {
        EditorPage? editorPage = ActiveEditorPage();
        bool hasEditor = editorPage is not null;
        bool canPromoteHeading = editorPage?.CanPromoteCurrentHeading == true;
        bool canDemoteHeading = editorPage?.CanDemoteCurrentHeading == true;
        bool canPromoteSubtree = editorPage?.CanPromoteCurrentSubtree == true;
        bool canDemoteSubtree = editorPage?.CanDemoteCurrentSubtree == true;

        NumberHeadingsButton.IsEnabled = hasEditor;
        RemoveHeadingNumbersButton.IsEnabled = hasEditor;
        NumberHeadingsMenuItem.IsEnabled = hasEditor;
        RemoveHeadingNumbersMenuItem.IsEnabled = hasEditor;

        PromoteHeadingButton.IsEnabled = canPromoteHeading;
        DemoteHeadingButton.IsEnabled = canDemoteHeading;
        PromoteHeadingMenuItem.IsEnabled = canPromoteHeading;
        DemoteHeadingMenuItem.IsEnabled = canDemoteHeading;

        PromoteSubtreeButton.IsEnabled = canPromoteSubtree;
        DemoteSubtreeButton.IsEnabled = canDemoteSubtree;
        PromoteSubtreeMenuItem.IsEnabled = canPromoteSubtree;
        DemoteSubtreeMenuItem.IsEnabled = canDemoteSubtree;
    }

    private void RemoveTabSubscriptions(DocumentViewModel viewModel)
    {
        viewModel.PropertyChanged -= TabViewModel_PropertyChanged;
        if (_tabItems.TryGetValue(viewModel, out TabViewItem? tab)
            && tab.Content is EditorPage editorPage)
        {
            editorPage.HeadingCommandStateChanged -= EditorPage_HeadingCommandStateChanged;
        }
    }

    private async Task<string?> PromptForInsertContentAsync(string title)
    {
        if (_isInsertDialogOpen)
        {
            return null;
        }

        _isInsertDialogOpen = true;
        try
        {
            var textBox = new TextBox
            {
                AcceptsReturn = true,
                MinHeight = 180,
                TextWrapping = TextWrapping.Wrap,
                PlaceholderText = "Enter Markdown to insert...",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "Cascadia Code, Cascadia Mono, Consolas, Courier New"),
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = textBox,
                PrimaryButtonText = "Insert",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? textBox.Text
                : null;
        }
        finally
        {
            _isInsertDialogOpen = false;
        }
    }
}
