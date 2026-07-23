using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface IServerInstaller
{
    Task InstallAsync(ServerProfile profile, string javaPath, Action<string> log, CancellationToken cancellationToken = default);
}

public sealed class ServerInstaller(HttpClient httpClient) : IServerInstaller
{
    private const string ForgeMavenMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

    public async Task InstallAsync(
        ServerProfile profile,
        string javaPath,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.DirectoryPath);
        Directory.CreateDirectory(Path.Combine(profile.DirectoryPath, "logs"));
        Directory.CreateDirectory(Path.Combine(profile.DirectoryPath, "backups"));

        if (File.Exists(profile.ServerJarPath) || !string.IsNullOrWhiteSpace(profile.ArgumentsFilePath))
        {
            return;
        }

        switch (profile.Loader)
        {
            case LoaderType.Fabric:
                await InstallFabricAsync(profile, log, cancellationToken);
                break;
            case LoaderType.Quilt:
                await InstallQuiltAsync(profile, log, cancellationToken);
                break;
            case LoaderType.Forge:
                await InstallForgeAsync(profile, javaPath, log, cancellationToken);
                break;
            case LoaderType.NeoForge:
                await InstallNeoForgeAsync(profile, javaPath, log, cancellationToken);
                break;
            default:
                await InstallVanillaAsync(profile, log, cancellationToken);
                break;
        }
    }

    private async Task InstallVanillaAsync(ServerProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        log("Сервер: скачивание vanilla server.jar.");
        var url = await GetVanillaServerUrlAsync(profile.MinecraftVersion, cancellationToken);
        await DownloadFileAsync(url, profile.ServerJarPath, cancellationToken);
    }

    private async Task InstallFabricAsync(ServerProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        log("Fabric: определение версии загрузчика.");
        var loaderVersion = string.IsNullOrWhiteSpace(profile.LoaderVersion)
            ? await GetJsonFirstStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{profile.MinecraftVersion}", "loader", "version", cancellationToken)
            : profile.LoaderVersion;

        var installerVersion = await GetJsonFirstStringAsync(
            "https://meta.fabricmc.net/v2/versions/installer",
            null,
            "version",
            cancellationToken);

        var url = $"https://meta.fabricmc.net/v2/versions/loader/{profile.MinecraftVersion}/{loaderVersion}/{installerVersion}/server/jar";
        log($"Fabric: скачивание server.jar ({loaderVersion}).");
        await DownloadFileAsync(url, profile.ServerJarPath, cancellationToken);
        profile.LoaderVersion = loaderVersion;
    }

    private async Task InstallQuiltAsync(ServerProfile profile, Action<string> log, CancellationToken cancellationToken)
    {
        log("Quilt: определение версии загрузчика.");
        var loaderVersion = string.IsNullOrWhiteSpace(profile.LoaderVersion)
            ? await GetJsonFirstStringAsync($"https://meta.quiltmc.org/v3/versions/loader/{profile.MinecraftVersion}", "loader", "version", cancellationToken)
            : profile.LoaderVersion;

        var installerVersion = await GetJsonFirstStringAsync(
            "https://meta.quiltmc.org/v3/versions/installer",
            null,
            "version",
            cancellationToken);

        var url = $"https://meta.quiltmc.org/v3/versions/loader/{profile.MinecraftVersion}/{loaderVersion}/{installerVersion}/server/jar";
        log($"Quilt: скачивание server.jar ({loaderVersion}).");
        await DownloadFileAsync(url, profile.ServerJarPath, cancellationToken);
        profile.LoaderVersion = loaderVersion;
    }

    private async Task InstallForgeAsync(ServerProfile profile, string javaPath, Action<string> log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.LoaderVersion))
        {
            log("Forge: detecting loader version.");
            profile.LoaderVersion = await GetLatestForgeLoaderVersionAsync(profile.MinecraftVersion, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(profile.LoaderVersion))
        {
            throw new InvalidOperationException("Для Forge нужна версия загрузчика.");
        }

        var version = $"{profile.MinecraftVersion}-{profile.LoaderVersion}";
        var installerName = $"forge-{version}-installer.jar";
        var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{version}/{installerName}";
        var installerPath = Path.Combine(profile.DirectoryPath, installerName);
        log($"Forge: скачивание installer.jar ({profile.LoaderVersion}).");
        await DownloadFileAsync(installerUrl, installerPath, cancellationToken);
        await RunInstallerWithRetriesAsync(javaPath, installerPath, profile.DirectoryPath, log, cancellationToken);

        profile.ArgumentsFilePath = FindArgumentsFile(profile.DirectoryPath, "forge") ??
                                    FindArgumentsFile(profile.DirectoryPath, "minecraftforge");
        profile.ServerJarPath = FindServerJar(profile.DirectoryPath, "forge") ?? profile.ServerJarPath;
    }

    private async Task<string> GetLatestForgeLoaderVersionAsync(
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidOperationException("Forge requires a Minecraft version.");
        }

        await using var stream = await httpClient.GetStreamAsync(ForgeMavenMetadataUrl, cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var prefix = minecraftVersion + "-";

        var loaderVersion = document
            .Descendants("version")
            .Select(element => element.Value.Trim())
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(version => version[prefix.Length..])
            .OrderByDescending(version => version, DottedVersionComparer.Instance)
            .FirstOrDefault();

        return loaderVersion ??
               throw new InvalidOperationException($"Could not find a Forge version for Minecraft {minecraftVersion}.");
    }

    private async Task InstallNeoForgeAsync(ServerProfile profile, string javaPath, Action<string> log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.LoaderVersion))
        {
            throw new InvalidOperationException("Для NeoForge нужна версия загрузчика.");
        }

        var installerName = $"neoforge-{profile.LoaderVersion}-installer.jar";
        var installerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{profile.LoaderVersion}/{installerName}";
        var installerPath = Path.Combine(profile.DirectoryPath, installerName);

        try
        {
            log($"NeoForge: скачивание installer.jar ({profile.LoaderVersion}).");
            await DownloadFileAsync(installerUrl, installerPath, cancellationToken);
        }
        catch
        {
            installerName = $"forge-{profile.LoaderVersion}-installer.jar";
            installerUrl = $"https://maven.neoforged.net/releases/net/neoforged/forge/{profile.LoaderVersion}/{installerName}";
            installerPath = Path.Combine(profile.DirectoryPath, installerName);
            await DownloadFileAsync(installerUrl, installerPath, cancellationToken);
        }

        await RunInstallerWithRetriesAsync(javaPath, installerPath, profile.DirectoryPath, log, cancellationToken);
        profile.ArgumentsFilePath = FindArgumentsFile(profile.DirectoryPath, "neoforge") ??
                                    FindArgumentsFile(profile.DirectoryPath, "forge");
        profile.ServerJarPath = FindServerJar(profile.DirectoryPath, "neoforge") ??
                                FindServerJar(profile.DirectoryPath, "forge") ??
                                profile.ServerJarPath;
    }

    private async Task<string> GetVanillaServerUrlAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            throw new InvalidOperationException("Не удалось определить версию Minecraft.");
        }

        using var manifest = await GetJsonAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
        var versionInfo = manifest.RootElement.GetProperty("versions")
            .EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("id").GetString() == minecraftVersion);

        if (versionInfo.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Версия Minecraft {minecraftVersion} не найдена в манифесте Mojang.");
        }

        var versionUrl = versionInfo.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("В манифесте Mojang нет URL версии.");
        using var versionManifest = await GetJsonAsync(versionUrl, cancellationToken);
        return versionManifest.RootElement
            .GetProperty("downloads")
            .GetProperty("server")
            .GetProperty("url")
            .GetString()
            ?? throw new InvalidOperationException("В манифесте Mojang нет server.jar.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        await using var stream = await httpClient.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<string> GetJsonFirstStringAsync(
        string url,
        string? objectProperty,
        string stringProperty,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(url, cancellationToken);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var source = objectProperty is null ? item : item.GetProperty(objectProperty);
            var value = source.GetProperty(stringProperty).GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException($"Не удалось получить версию с {url}.");
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var local = File.Create(destination);
        await remote.CopyToAsync(local, cancellationToken);
    }

    private static async Task RunInstallerAsync(
        string javaPath,
        string installerPath,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log("Installer: автоматическая установка сервера.");
        var startInfo = new ProcessStartInfo(javaPath, $"-jar \"{installerPath}\" --installServer")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить installer.jar.");

        var suppressedPatchingLines = 0;
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                LogInstallerLine(args.Data, log, ref suppressedPatchingLines);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                LogInstallerLine(args.Data, log, ref suppressedPatchingLines);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (suppressedPatchingLines > 0)
        {
            log($"Installer: skipped {suppressedPatchingLines} verbose patching lines.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Installer завершился с кодом {process.ExitCode}.");
        }
    }

    private static async Task RunInstallerWithRetriesAsync(
        string javaPath,
        string installerPath,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await RunInstallerAsync(javaPath, installerPath, workingDirectory, log, cancellationToken);
                return;
            }
            catch (InvalidOperationException) when (attempt < maxAttempts)
            {
                log($"Installer: attempt {attempt} failed, retrying...");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private static void LogInstallerLine(string line, Action<string> log, ref int suppressedPatchingLines)
    {
        if (IsVerboseInstallerLine(line))
        {
            suppressedPatchingLines++;
            if (suppressedPatchingLines % 1000 == 0)
            {
                log($"Installer: patching Minecraft classes... {suppressedPatchingLines} lines");
            }

            return;
        }

        log(line);
    }

    private static bool IsVerboseInstallerLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("Patching ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Reading patch ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Checksum:", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindArgumentsFile(string directory, string marker)
    {
        return Directory.EnumerateFiles(directory, "*args.txt", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
                                    Path.GetFileName(path).Contains("win", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindServerJar(string directory, string marker)
    {
        var jars = Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Contains("installer", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return jars.FirstOrDefault(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.Contains(marker, StringComparison.OrdinalIgnoreCase) &&
                       fileName.Contains("server", StringComparison.OrdinalIgnoreCase);
            }) ??
            jars.FirstOrDefault(path => Path.GetFileName(path).Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DottedVersionComparer : IComparer<string>
    {
        public static readonly DottedVersionComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var left = ParseVersionParts(x);
            var right = ParseVersionParts(y);
            var count = Math.Max(left.Length, right.Length);
            for (var index = 0; index < count; index++)
            {
                var leftPart = index < left.Length ? left[index] : 0;
                var rightPart = index < right.Length ? right[index] : 0;
                var comparison = leftPart.CompareTo(rightPart);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        private static int[] ParseVersionParts(string? version)
        {
            return Regex.Matches(version ?? "", @"\d+")
                .Select(match => int.TryParse(match.Value, out var value) ? value : 0)
                .ToArray();
        }
    }
}
