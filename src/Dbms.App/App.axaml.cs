using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dbms.App.Updates;
using Dbms.App.ViewModels;
using Dbms.Core.Abstractions;
using Dbms.Infrastructure.Providers;
using Dbms.Infrastructure.Storage;

namespace Dbms.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var registry = new DatabaseProviderRegistry(new IDatabaseProvider[]
            {
                new PostgreSqlProvider(),
                new MySqlProvider(),
                new SqlServerProvider(),
                new SqliteProvider()
            });
            var credentialStore = new ProtectedDataCredentialStore();
            var viewModel = new MainWindowViewModel(
                registry,
                new FileProfileStore(credentialStore),
                new HistoryStore(),
                new AppUpdateService());
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            _ = viewModel.LoadAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
