using System.ComponentModel;
using MDEdit.Application.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MDEdit.WinUI.Views;

public sealed partial class EditorPage : Page
{
    private bool _syncingFromViewModel;
    private bool _editingViewModel;

    public DocumentViewModel ViewModel { get; }

    public EditorPage(DocumentViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        this.InitializeComponent();

        SyncTextBoxToViewModel();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Unloaded -= OnUnloaded;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.RawMarkdown) && !_editingViewModel)
        {
            SyncTextBoxToViewModel();
        }
    }

    private void SyncTextBoxToViewModel()
    {
        _syncingFromViewModel = true;
        RawMarkdownTextBox.Text = ViewModel.RawMarkdown;
        _syncingFromViewModel = false;
    }

    private void RawMarkdownTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingFromViewModel)
        {
            return;
        }

        _editingViewModel = true;
        ViewModel.SetMarkdownFromEditor(RawMarkdownTextBox.Text);
        _editingViewModel = false;
    }
}
