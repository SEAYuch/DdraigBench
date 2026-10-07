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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var filePicker = new StorageProviderFilePicker(window);
            var viewModel = new MainViewModel(
                new ConnectionDialog(() => window, new FreeSqlConnectionTester()),
                new FreeSqlFactory(),
                new FreeSqlMetadataExplorer(),
                filePicker,
                new ExportDialog(() => window, filePicker),
                new DdlViewer(() => window),
                new SecretConnectionStoreFactory());

            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // 读已保存连接要探测系统密钥库（异步、可能阻塞），故等窗口打开后再启动，
            // 且工厂内部把它放到后台线程——绝不占 UI 线程。
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
