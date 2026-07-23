using System.Text.Json;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface IServerRepository
{
    Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken = default);
    Task<ServerProfile?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class ServerRepository(ISettingsService settingsService) : IServerRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        Directory.CreateDirectory(settings.ServersPath);

        var profiles = new List<ServerProfile>();
        foreach (var file in Directory.EnumerateFiles(settings.ServersPath, "server.json", SearchOption.AllDirectories))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var profile = await JsonSerializer.DeserializeAsync<ServerProfile>(stream, JsonOptions, cancellationToken);
                if (profile is not null)
                {
                    profile.Status = ServerStatus.Ready;
                    profiles.Add(profile);
                }
            }
            catch
            {
                // Broken metadata should not stop the launcher from opening.
            }
        }

        return profiles.OrderByDescending(profile => profile.UpdatedAt).ToArray();
    }

    public async Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.DirectoryPath);
        profile.UpdatedAt = DateTimeOffset.Now;
        var metadataPath = Path.Combine(profile.DirectoryPath, "server.json");
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
    }

    public async Task<ServerProfile?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var profiles = await LoadAsync(cancellationToken);
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
