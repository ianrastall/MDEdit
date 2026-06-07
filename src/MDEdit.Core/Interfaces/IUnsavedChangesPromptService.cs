namespace MDEdit.Core.Interfaces;

public interface IUnsavedChangesPromptService
{
    Task<bool> ConfirmDiscardUnsavedChangesAsync(int unsavedDocumentCount);
}
