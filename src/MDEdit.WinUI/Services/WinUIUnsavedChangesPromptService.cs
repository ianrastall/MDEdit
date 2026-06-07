using MDEdit.Core.Interfaces;
using Microsoft.UI.Xaml.Controls;

namespace MDEdit.WinUI.Services;

public sealed class WinUIUnsavedChangesPromptService : IUnsavedChangesPromptService
{
    private bool _isDialogOpen;

    public async Task<bool> ConfirmDiscardUnsavedChangesAsync(int unsavedDocumentCount)
    {
        if (_isDialogOpen)
        {
            return false;
        }

        Microsoft.UI.Xaml.XamlRoot? xamlRoot =
            (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow?.Content?.XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        string documentText = unsavedDocumentCount == 1
            ? "1 document has unsaved changes."
            : $"{unsavedDocumentCount:N0} documents have unsaved changes.";

        var dialog = new ContentDialog
        {
            Title = "Unsaved changes",
            Content = $"{documentText} Exit and discard those changes?",
            PrimaryButtonText = "Exit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        _isDialogOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }
}
