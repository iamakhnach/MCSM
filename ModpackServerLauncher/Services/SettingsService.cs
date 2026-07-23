using System.Text.Json;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsPath = Path.Combine(appData, "ModpackServerLauncher", "settings.json");
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            Directory.CreateDirectory(Current.ServersPath);
            await SaveAsync(Current, cancellationToken);
            return Current;
        }

        await using (var stream = File.OpenRead(_settingsPath))
        {
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();
        }

        if (IsOldDebugServersPath(Current.ServersPath))
        {
            Current.ServersPath = AppSettings.DefaultServersPath;
            await SaveAsync(Current, cancellationToken);
        }

        Directory.CreateDirectory(Current.ServersPath);
        return Current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        Directory.CreateDirectory(settings.ServersPath);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        Current = settings;
    }

    private static bool IsOldDebugServersPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appBase = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(Path.Combine(appBase, "Servers"), StringComparison.OrdinalIgnoreCase);
    }
}
