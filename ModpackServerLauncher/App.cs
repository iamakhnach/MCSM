using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using ModpackServerLauncher.Services;
using ModpackServerLauncher.ViewModels;
using ModpackServerLauncher.Views;

namespace ModpackServerLauncher;

public sealed class App : Application
{
    private ServiceProvider? _services;

    public static IServiceProvider Services =>
        ((App)Current!)._services ?? throw new InvalidOperationException("Services are not ready.");

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IServerRepository, ServerRepository>();
        services.AddSingleton<IArchiveReader, SharpCompressArchiveReader>();
        services.AddSingleton<IModpackAnalyzer, ModpackAnalyzer>();
        services.AddSingleton<IJavaProvisioner, JavaProvisioner>();
        services.AddSingleton<IServerInstaller, ServerInstaller>();
        services.AddSingleton<IServerProcessService, ServerProcessService>();
        services.AddSingleton<IServerMaintenanceService, ServerMaintenanceService>();
        services.AddSingleton<IImportWorkflow, ImportWorkflow>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
