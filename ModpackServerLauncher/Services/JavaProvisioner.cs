using System.Diagnostics;
using System.IO.Compression;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface IJavaProvisioner
{
    Task<string> EnsureJavaAsync(string minecraftVersion, Action<string> log, CancellationToken cancellationToken = default);
}

public sealed class JavaProvisioner(ISettingsService settingsService, HttpClient httpClient) : IJavaProvisioner
{
    public async Task<string> EnsureJavaAsync(
        string minecraftVersion,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var requiredMajor = RequiredJavaMajor(minecraftVersion);

        if (!string.IsNullOrWhiteSpace(settings.JavaPath) && File.Exists(settings.JavaPath))
        {
            log($"Java: используется путь из настроек ({settings.JavaPath}).");
            return settings.JavaPath;
        }

        var bundled = FindBundledJava(requiredMajor);
        if (bundled is not null)
        {
            log($"Java {requiredMajor}: найдена встроенная установка.");
            return bundled;
        }

        if (await IsJavaOnPathAcceptableAsync(requiredMajor, cancellationToken))
        {
            log("Java: используется java из PATH.");
            return "java";
        }

        log($"Java {requiredMajor}: скачивание Eclipse Temurin JRE.");
        return await DownloadTemurinAsync(requiredMajor, log, cancellationToken);
    }

    private static int RequiredJavaMajor(string minecraftVersion)
    {
        if (Version.TryParse(NormalizeVersion(minecraftVersion), out var version))
        {
            if (version.Major > 1 || version.Minor > 20 || version.Minor == 20 && version.Build >= 5)
            {
                return 21;
            }

            if (version.Minor >= 18)
            {
                return 17;
            }

            if (version.Minor >= 17)
            {
                return 16;
            }
        }

        return 8;
    }

    private static string NormalizeVersion(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => value + ".0.0",
            2 => value + ".0",
            _ => value
        };
    }

    private static string? FindBundledJava(int major)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModpackServerLauncher",
            "Java",
            $"jre-{major}");

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, "java.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> IsJavaOnPathAcceptableAsync(int requiredMajor, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo("java", "-version")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return ParseJavaMajor(output) >= requiredMajor;
        }
        catch
        {
            return false;
        }
    }

    private static int ParseJavaMajor(string output)
    {
        var marker = "version \"";
        var start = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return 0;
        }

        start += marker.Length;
        var end = output.IndexOf('"', start);
        if (end <= start)
        {
            return 0;
        }

        var version = output[start..end];
        if (version.StartsWith("1.", StringComparison.Ordinal))
        {
            return int.TryParse(version.Split('.').ElementAtOrDefault(1), out var legacyMajor) ? legacyMajor : 0;
        }

        return int.TryParse(version.Split('.').FirstOrDefault(), out var major) ? major : 0;
    }

    private async Task<string> DownloadTemurinAsync(int major, Action<string> log, CancellationToken cancellationToken)
    {
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModpackServerLauncher",
            "Java",
            $"jre-{major}");

        Directory.CreateDirectory(installRoot);
        var archivePath = Path.Combine(installRoot, "jre.zip");
        var url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jre/hotspot/normal/eclipse";

        await using (var remote = await httpClient.GetStreamAsync(url, cancellationToken))
        await using (var local = File.Create(archivePath))
        {
            await remote.CopyToAsync(local, cancellationToken);
        }

        log("Java: распаковка.");
        ZipFile.ExtractToDirectory(archivePath, installRoot, overwriteFiles: true);
        File.Delete(archivePath);

        var javaPath = FindBundledJava(major);
        if (javaPath is null)
        {
            throw new InvalidOperationException("Java была скачана, но java.exe не найден.");
        }

        return javaPath;
    }
}
