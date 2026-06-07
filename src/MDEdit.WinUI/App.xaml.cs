using MDEdit.Application.ViewModels;
using MDEdit.Core.Interfaces;
using MDEdit.Infrastructure.Services;
using MDEdit.WinUI.Services;
using MDEdit.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading;
using WinRT.Interop;

namespace MDEdit.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private bool _allowMainWindowClose;
    private bool _isHandlingMainWindowClose;

    public Window? MainWindow { get; private set; }
    public IHost Host { get; }

    public App()
    {
        this.InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IMarkdownFormattingService, MarkdownFormattingService>();
                services.AddSingleton<IFilePickerService, WinUIFilePickerService>();
                services.AddSingleton<IApplicationService, WinUIApplicationService>();
                services.AddSingleton<IUnsavedChangesPromptService, WinUIUnsavedChangesPromptService>();
                services.AddTransient<DocumentViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddTransient<ShellPage>();
            })
            .Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherQueueSynchronizationContext(dispatcherQueue));

        MainWindow = new Window { Title = "MDEdit" };

        var rootFrame = new Frame();
        rootFrame.Navigate(typeof(ShellPage));
        MainWindow.Content = rootFrame;
        AppWindow appWindow = GetAppWindow(MainWindow);
        appWindow.Closing += MainWindow_Closing;
        MaximizeWindow(appWindow);
        MainWindow.Activate();

        if (rootFrame.Content is ShellPage shellPage)
        {
            string? launchPath = GetLaunchFilePath(args.Arguments);
            if (launchPath is not null)
            {
                _ = shellPage.OpenExternalFileAsync(launchPath);
            }
        }
    }

    private static string? GetLaunchFilePath(string launchArguments)
    {
        string? fromArgs = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
        if (fromArgs is not null)
        {
            return fromArgs;
        }

        string trimmed = launchArguments.Trim().Trim('"');
        return File.Exists(trimmed) ? trimmed : null;
    }

    public void CloseMainWindowWithoutPrompt()
    {
        _allowMainWindowClose = true;
        MainWindow?.Close();
    }

    private async void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowMainWindowClose)
        {
            return;
        }

        ShellViewModel shellViewModel = Host.Services.GetRequiredService<ShellViewModel>();
        if (!shellViewModel.HasUnsavedChanges)
        {
            return;
        }

        args.Cancel = true;
        if (_isHandlingMainWindowClose)
        {
            return;
        }

        _isHandlingMainWindowClose = true;
        try
        {
            if (await shellViewModel.ConfirmCloseAsync())
            {
                CloseMainWindowWithoutPrompt();
            }
        }
        finally
        {
            _isHandlingMainWindowClose = false;
        }
    }

    private static AppWindow GetAppWindow(Window window)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private static void MaximizeWindow(AppWindow appWindow)
    {
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }
}
