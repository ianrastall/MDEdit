using MDEdit.Core.Interfaces;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MDEdit.WinUI.Services;

public sealed class WinUIFilePickerService : IFilePickerService
{
    public async Task<string?> PickOpenMarkdownFileAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".mdown");
        picker.FileTypeFilter.Add(".mkdn");
        picker.FileTypeFilter.Add(".txt");

        InitializeWithMainWindow(picker);
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickSaveMarkdownFileAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            DefaultFileExtension = ".md",
        };

        picker.FileTypeChoices.Add("Markdown", [".md"]);
        picker.FileTypeChoices.Add("Plain Text", [".txt"]);

        InitializeWithMainWindow(picker);
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static void InitializeWithMainWindow(object picker)
    {
        Window window = (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow
            ?? throw new InvalidOperationException("Main window is not available.");

        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Main window handle is not available.");
        }

        InitializeWithWindow.Initialize(picker, hwnd);
    }
}
