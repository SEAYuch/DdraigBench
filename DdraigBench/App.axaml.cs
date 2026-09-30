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
            window.DataContext = new MainViewModel(
                new ConnectionDialog(() => window, new FreeSqlConnectionTester()),
                new FreeSqlFactory(),
                new FreeSqlMetadataExplorer(),
                filePicker,
                new ExportDialog(() => window, filePicker),
                new DdlViewer(() => window));

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
