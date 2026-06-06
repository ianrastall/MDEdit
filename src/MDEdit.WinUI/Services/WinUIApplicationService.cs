using MDEdit.Core.Interfaces;

namespace MDEdit.WinUI.Services;

public sealed class WinUIApplicationService : IApplicationService
{
    public void Exit()
    {
        (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow?.Close();
    }
}
