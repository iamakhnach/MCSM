using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface IServerMaintenanceService
{
    Task EnsureLanAccessAsync(ServerProfile profile, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetLanAddresses();
    Task ClearAllServerDataAsync(CancellationToken cancellationToken = default);
    Task ResetApplicationAsync(CancellationToken cancellationToken = default);
}

public sealed class ServerMaintenanceService(ISettingsService settingsService) : IServerMaintenanceService
{
    public async Task EnsureLanAccessAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.DirectoryPath);
        var propertiesPath = Path.Combine(profile.DirectoryPath, "server.properties");
        var lines = File.Exists(propertiesPath)
            ? (await File.ReadAllLinesAsync(propertiesPath, cancellationToken)).ToList()
            : [];

        SetProperty(lines, "server-ip", "");
        SetProperty(lines, "server-port", "25565");
        SetProperty(lines, "enable-status", "true");

        await File.WriteAllLinesAsync(propertiesPath, lines, cancellationToken);
    }

    public IReadOnlyList<string> GetLanAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address)
            .Where(address => !IPAddress.IsLoopback(address) &&
                              !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task ClearAllServerDataAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        foreach (var path in GetCleanupPaths(settings.ServersPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearDirectoryContents(path);
        }

        Directory.CreateDirectory(settings.ServersPath);
    }

    public async Task ResetApplicationAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        foreach (var path in GetCleanupPaths(settings.ServersPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearDirectoryContents(path);
        }

        ClearOwnedAppDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModpackServerLauncher"));
        ClearOwnedAppDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModpackServerLauncher"));

        await settingsService.SaveAsync(new AppSettings(), cancellationToken);
    }

    private static IEnumerable<string> GetCleanupPaths(string configuredServersPath)
    {
        return new[]
            {
                configuredServersPath,
                Path.Combine(AppContext.BaseDirectory, "Servers"),
                Path.Combine(Environment.CurrentDirectory, "Servers")
            }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void ClearDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory) || !IsReasonableCleanupTarget(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            Directory.Delete(childDirectory, recursive: true);
        }
    }

    private static void ClearOwnedAppDirectory(string directory)
    {
        if (!Directory.Exists(directory) || !IsOwnedAppDirectory(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            Directory.Delete(childDirectory, recursive: true);
        }
    }

    private static bool IsReasonableCleanupTarget(string directory)
    {
        var fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Length > 10 &&
               !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) &&
               fullPath.EndsWith($"{Path.DirectorySeparatorChar}Servers", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedAppDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Length > 10 &&
               fullPath.EndsWith($"{Path.DirectorySeparatorChar}ModpackServerLauncher", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetProperty(List<string> lines, string key, string value)
    {
        var prefix = key + "=";
        var existingIndex = lines.FindIndex(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var lineValue = prefix + value;
        if (existingIndex >= 0)
        {
            lines[existingIndex] = lineValue;
        }
        else
        {
            lines.Add(lineValue);
        }
    }
}
