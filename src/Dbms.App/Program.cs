using Avalonia;
using Dbms.App.Updates;
using Velopack;

namespace Dbms.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!PackageIdentity.IsPackaged)
        {
            VelopackApp.Build().Run();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
