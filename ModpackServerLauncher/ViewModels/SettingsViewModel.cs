using CommunityToolkit.Mvvm.ComponentModel;
using ModpackServerLauncher.Models;
using ModpackServerLauncher.Services;

namespace ModpackServerLauncher.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settingsService) : ObservableObject
{
    [ObservableProperty]
    private string _serversPath = "";

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private int _maxMemoryGb = 8;

    [ObservableProperty]
    private bool _darkTheme = true;

    [ObservableProperty]
    private bool _autoUpdate = true;

    public async Task LoadAsync()
    {
        var settings = await settingsService.LoadAsync();
        ServersPath = settings.ServersPath;
        JavaPath = settings.JavaPath ?? "";
        MaxMemoryGb = settings.MaxMemoryGb;
        DarkTheme = settings.DarkTheme;
        AutoUpdate = settings.AutoUpdate;
    }

    public async Task SaveAsync()
    {
        await settingsService.SaveAsync(new AppSettings
        {
            ServersPath = ServersPath,
            JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath,
            MaxMemoryGb = MaxMemoryGb,
            DarkTheme = DarkTheme,
            AutoUpdate = AutoUpdate
        });
    }
}
