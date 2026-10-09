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
    private readonly ThemePreferenceStore _themeStore = new();

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
                CurrentThemeMode = await _themeStore.LoadAsync();
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
                _themeStore,
                SetThemeModeAsync);

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // 读已保存连接要探测系统密钥库（异步、可能阻塞），故等窗口打开后再启动，
            // 且工厂内部把它放到后台线程——绝不占 UI 线程。
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
