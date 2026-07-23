using Avalonia;
using ModpackServerLauncher.Services;

namespace ModpackServerLauncher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                AppLogger.Error(exception, "Unhandled exception");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error(eventArgs.Exception, "Unobserved task exception");
            eventArgs.SetObserved();
        };

        try
        {
            AppLogger.Info("Application started.");
            if (args.Contains("--smoke-tests", StringComparer.OrdinalIgnoreCase))
            {
                SmokeTests.RunAsync().GetAwaiter().GetResult();
                return;
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Fatal exception");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
