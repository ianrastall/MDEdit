using MDEdit.Core.Interfaces;

namespace MDEdit.WinUI.Services;

public sealed class WinUIApplicationService : IApplicationService
{
    public Task ExitAsync()
    {
        return Microsoft.UI.Xaml.Application.Current is App app
            ? app.CloseMainWindowWithoutPromptAsync()
            : Task.CompletedTask;
    }
}
