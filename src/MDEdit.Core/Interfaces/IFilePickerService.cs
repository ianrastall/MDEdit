namespace MDEdit.Core.Interfaces;

public interface IFilePickerService
{
    Task<string?> PickOpenMarkdownFileAsync();
    Task<string?> PickSaveMarkdownFileAsync(string suggestedFileName);
}
