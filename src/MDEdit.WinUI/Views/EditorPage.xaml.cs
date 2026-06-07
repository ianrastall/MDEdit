using System.ComponentModel;
using System.Text;
using MDEdit.Application.ViewModels;
using MDEdit.Core.Models;
using MDEdit.Core.Utilities;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MDEdit.WinUI.Views;

public sealed partial class EditorPage : Page
{
    private bool _syncingFromViewModel;
    private bool _editingViewModel;
    private bool _reflowRadioButtonsReady;
    private bool _currentLineHighlightUpdateQueued;
    private ScrollViewer? _editorScrollViewer;
    private int _lineNumberCount;

    public DocumentViewModel ViewModel { get; }

    public EditorPage(DocumentViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        this.InitializeComponent();

        SyncTextBoxToViewModel();
        _reflowRadioButtonsReady = true;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Unloaded -= OnUnloaded;

        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ViewChanged -= EditorScrollViewer_ViewChanged;
            _editorScrollViewer = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.RawMarkdown) && !_editingViewModel)
        {
            SyncTextBoxToViewModel();
        }

        if (e.PropertyName is nameof(DocumentViewModel.IsEditorVisible)
            or nameof(DocumentViewModel.IsPreviewVisible))
        {
            SyncEditorChrome();
        }
    }

    private void SyncTextBoxToViewModel()
    {
        if (_syncingFromViewModel)
        {
            return;
        }

        string normalizedTextBoxText =
            MarkdownTextUtilities.NormalizeLineEndingsToCrlf(RawMarkdownTextBox.Text);
        if (normalizedTextBoxText == ViewModel.RawMarkdown)
        {
            SyncEditorChrome();
            return;
        }

        int selectionStart = RawMarkdownTextBox.SelectionStart;
        int selectionLength = RawMarkdownTextBox.SelectionLength;
        double? horizontalOffset = _editorScrollViewer?.HorizontalOffset;
        double? verticalOffset = _editorScrollViewer?.VerticalOffset;

        _syncingFromViewModel = true;
        try
        {
            RawMarkdownTextBox.Text = ViewModel.RawMarkdown;

            int restoredSelectionStart = Math.Clamp(selectionStart, 0, RawMarkdownTextBox.Text.Length);
            RawMarkdownTextBox.SelectionStart = restoredSelectionStart;
            RawMarkdownTextBox.SelectionLength = Math.Clamp(
                selectionLength,
                0,
                RawMarkdownTextBox.Text.Length - restoredSelectionStart);
        }
        finally
        {
            _syncingFromViewModel = false;
        }

        if (horizontalOffset is not null || verticalOffset is not null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _editorScrollViewer?.ChangeView(
                    horizontalOffset: horizontalOffset,
                    verticalOffset: verticalOffset,
                    zoomFactor: null,
                    disableAnimation: true);
                SyncLineNumberScroll();
                UpdateCurrentLineHighlight();
            });
        }

        SyncEditorChrome();
    }

    private void RawMarkdownTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFromViewModel)
        {
            return;
        }

        _editingViewModel = true;
        try
        {
            ViewModel.SetMarkdownFromEditor(RawMarkdownTextBox.Text);
        }
        finally
        {
            _editingViewModel = false;
        }

        SyncEditorChrome();
    }

    private void RawMarkdownTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (_editorScrollViewer is not null)
        {
            return;
        }

        _editorScrollViewer = FindVisualChild<ScrollViewer>(RawMarkdownTextBox);
        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;
        }

        SyncEditorChrome();
    }

    private void RawMarkdownTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateCurrentLineHighlight();
    }

    private void RawMarkdownTextBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCurrentLineHighlight();
    }

    private void EditorScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        SyncLineNumberScroll();
        UpdateCurrentLineHighlight();
    }

    public void InsertAtCursor(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        ViewModel.IsPreviewMode = false;

        int position = Math.Clamp(RawMarkdownTextBox.SelectionStart, 0, RawMarkdownTextBox.Text.Length);
        string current = RawMarkdownTextBox.Text;
        string updated = current[..position] + content + current[position..];

        _syncingFromViewModel = true;
        try
        {
            RawMarkdownTextBox.Text = updated;
            RawMarkdownTextBox.SelectionStart = position + content.Length;
            RawMarkdownTextBox.SelectionLength = 0;
        }
        finally
        {
            _syncingFromViewModel = false;
        }

        _editingViewModel = true;
        try
        {
            ViewModel.SetMarkdownFromEditor(updated);
            string normalizedUpdated = MarkdownTextUtilities.NormalizeLineEndingsToCrlf(updated);
            if (ViewModel.RawMarkdown != normalizedUpdated)
            {
                SyncTextBoxToNormalizedText(updated, position + content.Length, 0);
            }
        }
        finally
        {
            _editingViewModel = false;
        }

        ViewModel.StatusMessage = "Content inserted.";

        RawMarkdownTextBox.Focus(FocusState.Programmatic);
        SyncEditorChrome();
    }

    private void ReflowTopHeadingRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_reflowRadioButtonsReady)
        {
            return;
        }

        if (sender is RadioButton radioButton
            && int.TryParse(radioButton.Tag?.ToString(), out int topHeadingLevel))
        {
            ViewModel.ReflowToTopHeadingLevel(topHeadingLevel);
        }
    }

    public void ToggleOutlinePane()
    {
        ViewModel.ToggleOutlineVisibility();
    }

    private void HeadingTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (TryGetHeadingNode(sender.SelectedItem, out HeadingNode? heading) && heading is not null)
        {
            NavigateToHeading(heading);
            sender.SelectedItem = null;
        }
    }

    private void NavigateToHeading(HeadingNode heading)
    {
        ViewModel.IsPreviewMode = false;

        int offset = Math.Clamp(heading.CharacterOffset, 0, RawMarkdownTextBox.Text.Length);
        RawMarkdownTextBox.SelectionStart = offset;
        RawMarkdownTextBox.SelectionLength = 0;
        RawMarkdownTextBox.Focus(FocusState.Programmatic);
        SyncEditorChrome();
    }

    private void SyncEditorChrome()
    {
        UpdateLineNumbers();
        SyncLineNumberScroll();
        UpdateCurrentLineHighlight();
    }

    private void SyncTextBoxToNormalizedText(
        string originalText,
        int selectionStart,
        int selectionLength)
    {
        int normalizedStart = MarkdownTextUtilities.NormalizeLineEndingsToCrlf(
            originalText[..Math.Clamp(selectionStart, 0, originalText.Length)]).Length;
        int normalizedEnd = MarkdownTextUtilities.NormalizeLineEndingsToCrlf(
            originalText[..Math.Clamp(selectionStart + selectionLength, 0, originalText.Length)]).Length;

        _syncingFromViewModel = true;
        try
        {
            RawMarkdownTextBox.Text = ViewModel.RawMarkdown;
            RawMarkdownTextBox.SelectionStart = Math.Clamp(normalizedStart, 0, RawMarkdownTextBox.Text.Length);
            RawMarkdownTextBox.SelectionLength = Math.Max(0, normalizedEnd - normalizedStart);
        }
        finally
        {
            _syncingFromViewModel = false;
        }
    }

    private void UpdateLineNumbers()
    {
        int lineCount = MarkdownTextUtilities.CountLines(RawMarkdownTextBox.Text);
        if (lineCount == _lineNumberCount)
        {
            return;
        }

        var builder = new StringBuilder();
        for (int lineNumber = 1; lineNumber <= lineCount; lineNumber++)
        {
            if (lineNumber > 1)
            {
                builder.Append('\n');
            }

            builder.Append(lineNumber);
        }

        LineNumberTextBlock.Text = builder.ToString();
        _lineNumberCount = lineCount;
    }

    private void SyncLineNumberScroll()
    {
        if (_editorScrollViewer is null)
        {
            return;
        }

        LineNumberScrollViewer.ChangeView(
            horizontalOffset: null,
            verticalOffset: _editorScrollViewer.VerticalOffset,
            zoomFactor: null,
            disableAnimation: true);
    }

    private void UpdateCurrentLineHighlight()
    {
        if (_currentLineHighlightUpdateQueued)
        {
            return;
        }

        _currentLineHighlightUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _currentLineHighlightUpdateQueued = false;
            UpdateCurrentLineHighlightCore();
        }))
        {
            _currentLineHighlightUpdateQueued = false;
        }
    }

    private void UpdateCurrentLineHighlightCore()
    {
        if (EditorSurface.Visibility != Visibility.Visible || RawMarkdownTextBox.ActualHeight <= 0)
        {
            CurrentLineHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        Rect caretRect;
        try
        {
            caretRect = GetCaretRect();
        }
        catch (ArgumentException)
        {
            CurrentLineHighlight.Visibility = Visibility.Collapsed;
            return;
        }
        catch (InvalidOperationException)
        {
            CurrentLineHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        if (caretRect.Height <= 0
            || double.IsNaN(caretRect.Y)
            || double.IsInfinity(caretRect.Y))
        {
            CurrentLineHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        double top = Math.Max(0, caretRect.Y - 1);
        if (top > RawMarkdownTextBox.ActualHeight)
        {
            CurrentLineHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        CurrentLineHighlightTransform.Y = top;
        CurrentLineHighlight.Height = Math.Max(20, caretRect.Height + 2);
        CurrentLineHighlight.Visibility = Visibility.Visible;
    }

    private Rect GetCaretRect()
    {
        string text = RawMarkdownTextBox.Text;
        int position = Math.Clamp(RawMarkdownTextBox.SelectionStart, 0, text.Length);

        if (text.Length == 0)
        {
            return RawMarkdownTextBox.GetRectFromCharacterIndex(0, false);
        }

        return position >= text.Length
            ? RawMarkdownTextBox.GetRectFromCharacterIndex(text.Length - 1, true)
            : RawMarkdownTextBox.GetRectFromCharacterIndex(position, false);
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static bool TryGetHeadingNode(object? item, out HeadingNode? heading)
    {
        heading = item switch
        {
            HeadingNode node => node,
            TreeViewItem treeViewItem => treeViewItem.Tag as HeadingNode ?? treeViewItem.DataContext as HeadingNode,
            TreeViewNode treeViewNode => treeViewNode.Content as HeadingNode,
            _ => null,
        };

        return heading is not null;
    }
}
