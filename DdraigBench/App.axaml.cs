using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DdraigBench.Core.Connections;
using DdraigBench.Core.Metadata;
using DdraigBench.Services;
using DdraigBench.ViewModels;
using DdraigBench.Views;

namespace DdraigBench;

public partial class App : Application
{
    private readonly UiSettingsStore _uiSettingsStore = new();
    private bool _shutdownRequested;

    public ThemeMode CurrentThemeMode { get; private set; } = ThemeMode.System;

    public async Task SetThemeModeAsync(ThemeMode mode)
    {
        CurrentThemeMode = mode;
        RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => Avalonia.Styling.ThemeVariant.Light,
            ThemeMode.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };

        await Task.CompletedTask;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                CurrentThemeMode = (await _uiSettingsStore.LoadAsync()).Theme;
            }
            catch (IOException)
            {
                CurrentThemeMode = ThemeMode.System;
            }
            RequestedThemeVariant = CurrentThemeMode switch
            {
                ThemeMode.Light => Avalonia.Styling.ThemeVariant.Light,
                ThemeMode.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };

            var window = new MainWindow();
            var filePicker = new StorageProviderFilePicker(window);
            var viewModel = new MainViewModel(
                new ConnectionDialog(() => window, new FreeSqlConnectionTester()),
                new FreeSqlFactory(),
                new FreeSqlMetadataExplorer(),
                filePicker,
                new ExportDialog(() => window, filePicker),
                new DdlViewer(() => window),
                new SecretConnectionStoreFactory(),
                _uiSettingsStore,
                SetThemeModeAsync);

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // 读已保存连接要探测系统密钥库（异步、可能阻塞），故等窗口打开后再启动，
            // 且工厂内部把它放到后台线程——绝不占 UI 线程。
            window.Opened += async (_, _) => await viewModel.InitializeAsync();

            // 退出前把会话（连带 FreeSql 连接池）确定性处置掉：先取消这次退出，处置完再真正关闭。
            desktop.ShutdownRequested += (_, e) =>
            {
                if (_shutdownRequested)
                {
                    return; // 第二次请求来自下面的 Shutdown()，放行即可。
                }

                _shutdownRequested = true;
                e.Cancel = true;
                _ = DisposeThenShutdownAsync(desktop, viewModel);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>退出路径：会话处置失败也没有可上报的地方，无论如何都要把窗口关掉。</summary>
    private static async Task DisposeThenShutdownAsync(
        IClassicDesktopStyleApplicationLifetime desktop, MainViewModel viewModel)
    {
        try
        {
            await viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"关闭时处置会话失败：{ex.Message}");
        }
        finally
        {
            desktop.Shutdown();
        }
    }
}
