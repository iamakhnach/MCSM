using System.Text.Json;
using System.Text.RegularExpressions;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface IModpackAnalyzer
{
    Task<ModpackInfo> AnalyzeAsync(string archivePath, CancellationToken cancellationToken = default);
}

public sealed class ModpackAnalyzer(IArchiveReader archiveReader) : IModpackAnalyzer
{
    private static readonly Regex StrongMinecraftVersionRegex = new(
        @"(?i)(?:^|[^a-z0-9])(?:mc|minecraft|forge|fabric|quilt|neoforge)[_\-\s]?(?:v(?:ersion)?)?[_\-\s]?(1\.(?:[2-9]|[1-9]\d)(?:\.\d+)?)(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex MinecraftVersionRegex = new(
        @"(?<!\d)(1\.(?:[2-9]|[1-9]\d)(?:\.\d+)?)(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex LoaderMarkerRegex = new(
        @"(?i)(?:^|[^a-z0-9])(forge|fabric|quilt|neoforge)(?:[^a-z0-9]|$)",
        RegexOptions.Compiled);

    public async Task<ModpackInfo> AnalyzeAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var entries = await archiveReader.ListEntriesAsync(archivePath, cancellationToken);
        var modrinthIndex = entries.FirstOrDefault(entry =>
            IsEntryNamed(entry.Key, "modrinth.index.json"));
        if (modrinthIndex is not null)
        {
            var text = await archiveReader.ReadTextAsync(archivePath, modrinthIndex.Key, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return ParseMrPack(text, archivePath);
            }
        }

        var curseManifest = entries.FirstOrDefault(entry =>
            entry.Key.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (curseManifest is not null)
        {
            var text = await archiveReader.ReadTextAsync(archivePath, curseManifest.Key, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var parsed = TryParseCurseForge(text, archivePath);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        return InferFromEntries(entries, archivePath);
    }

    private static ModpackInfo ParseMrPack(string json, string archivePath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var dependencies = root.GetProperty("dependencies");
        var loader = LoaderType.Vanilla;
        string? loaderVersion = null;

        if (dependencies.TryGetProperty("forge", out var forge))
        {
            loader = LoaderType.Forge;
            loaderVersion = forge.GetString();
        }
        else if (dependencies.TryGetProperty("fabric-loader", out var fabric))
        {
            loader = LoaderType.Fabric;
            loaderVersion = fabric.GetString();
        }
        else if (dependencies.TryGetProperty("neoforge", out var neoforge))
        {
            loader = LoaderType.NeoForge;
            loaderVersion = neoforge.GetString();
        }
        else if (dependencies.TryGetProperty("quilt-loader", out var quilt))
        {
            loader = LoaderType.Quilt;
            loaderVersion = quilt.GetString();
        }

        var files = new List<MrPackFile>();
        if (root.TryGetProperty("files", out var fileArray) && fileArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in fileArray.EnumerateArray())
            {
                var env = file.TryGetProperty("env", out var envElement) ? envElement : default;
                var serverValue = env.ValueKind == JsonValueKind.Object &&
                                  env.TryGetProperty("server", out var serverElement)
                    ? serverElement.GetString()
                    : "required";

                if (string.Equals(serverValue, "unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var downloads = file.TryGetProperty("downloads", out var downloadArray) &&
                                downloadArray.ValueKind == JsonValueKind.Array
                    ? downloadArray.EnumerateArray().Select(item => item.GetString()).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
                    : null;

                files.Add(new MrPackFile
                {
                    Path = file.GetProperty("path").GetString() ?? "",
                    PrimaryDownloadUrl = downloads,
                    ServerRequired = string.Equals(serverValue, "required", StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        return new ModpackInfo
        {
            Name = CleanName(root.TryGetProperty("name", out var name) ? name.GetString() : null, archivePath),
            MinecraftVersion = dependencies.TryGetProperty("minecraft", out var minecraft) ? minecraft.GetString() ?? "" : "",
            Loader = loader,
            LoaderVersion = loaderVersion,
            IsMrPack = true,
            MrPackFiles = files
        };
    }

    private static ModpackInfo? TryParseCurseForge(string json, string archivePath)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("minecraft", out var minecraft))
            {
                return null;
            }

            var loader = LoaderType.Vanilla;
            string? loaderVersion = null;
            if (minecraft.TryGetProperty("modLoaders", out var modLoaders) &&
                modLoaders.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modLoaders.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
                    (loader, loaderVersion) = ParseLoaderId(id);
                    if (loader != LoaderType.Unknown)
                    {
                        break;
                    }
                }
            }

            return new ModpackInfo
            {
                Name = CleanName(root.TryGetProperty("name", out var name) ? name.GetString() : null, archivePath),
                MinecraftVersion = minecraft.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "",
                Loader = loader,
                LoaderVersion = loaderVersion
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ModpackInfo InferFromEntries(IReadOnlyList<ArchiveEntryInfo> entries, string archivePath)
    {
        var loader = InferLoader(entries);
        var minecraftVersion = InferMinecraftVersion(entries);

        return new ModpackInfo
        {
            Name = CleanName(null, archivePath),
            Loader = loader,
            MinecraftVersion = minecraftVersion,
            LoaderVersion = null
        };
    }

    private static LoaderType InferLoader(IReadOnlyList<ArchiveEntryInfo> entries)
    {
        if (entries.Any(entry => entry.Key.Contains("/fabric.mod.json", StringComparison.OrdinalIgnoreCase)))
        {
            return LoaderType.Fabric;
        }

        var scores = new Dictionary<LoaderType, int>
        {
            [LoaderType.Forge] = 0,
            [LoaderType.Fabric] = 0,
            [LoaderType.NeoForge] = 0,
            [LoaderType.Quilt] = 0
        };

        foreach (var entry in entries.Where(entry => !entry.IsDirectory && entry.Key.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            var fileName = GetFileName(entry.Key);
            foreach (Match match in LoaderMarkerRegex.Matches(fileName))
            {
                var loader = match.Groups[1].Value.ToLowerInvariant() switch
                {
                    "forge" => LoaderType.Forge,
                    "fabric" => LoaderType.Fabric,
                    "neoforge" => LoaderType.NeoForge,
                    "quilt" => LoaderType.Quilt,
                    _ => LoaderType.Unknown
                };

                if (loader != LoaderType.Unknown)
                {
                    scores[loader] += 1;
                }
            }
        }

        var winner = scores
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .FirstOrDefault();

        return winner.Value > 0 ? winner.Key : LoaderType.Unknown;
    }

    private static string InferMinecraftVersion(IReadOnlyList<ArchiveEntryInfo> entries)
    {
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            var fileName = GetFileName(entry.Key);
            AddVersionScores(scores, fileName, StrongMinecraftVersionRegex, 8);
            AddVersionScores(scores, fileName, MinecraftVersionRegex, 1);
        }

        return scores
            .OrderByDescending(pair => pair.Value)
            .ThenByDescending(pair => pair.Key.Count(character => character == '.'))
            .ThenByDescending(pair => pair.Key, MinecraftVersionComparer.Instance)
            .Select(pair => pair.Key)
            .FirstOrDefault() ?? "";
    }

    private static void AddVersionScores(
        IDictionary<string, int> scores,
        string text,
        Regex regex,
        int score)
    {
        foreach (Match match in regex.Matches(text))
        {
            var version = match.Groups[1].Value;
            if (!IsLikelyMinecraftVersion(version))
            {
                continue;
            }

            scores.TryGetValue(version, out var current);
            scores[version] = current + score;
        }
    }

    private static (LoaderType Loader, string? Version) ParseLoaderId(string id)
    {
        var parts = id.Split('-', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (LoaderType.Unknown, null);
        }

        return parts[0].ToLowerInvariant() switch
        {
            "forge" => (LoaderType.Forge, parts.ElementAtOrDefault(1)),
            "fabric" => (LoaderType.Fabric, parts.ElementAtOrDefault(1)),
            "neoforge" => (LoaderType.NeoForge, parts.ElementAtOrDefault(1)),
            "quilt" => (LoaderType.Quilt, parts.ElementAtOrDefault(1)),
            _ => (LoaderType.Unknown, parts.ElementAtOrDefault(1))
        };
    }

    private static string CleanName(string? name, string archivePath)
    {
        var rawName = string.IsNullOrWhiteSpace(name)
            ? GetFallbackName(archivePath)
            : name;

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            rawName = rawName.Replace(invalid, '-');
        }

        return rawName.Trim();
    }

    private static string GetFallbackName(string archivePath)
    {
        if (Directory.Exists(archivePath))
        {
            return new DirectoryInfo(archivePath).Name.TrimStart('.');
        }

        return Path.GetFileNameWithoutExtension(archivePath);
    }

    private static bool IsEntryNamed(string key, string fileName)
    {
        return key.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFileName(string key)
    {
        var normalized = key.Replace('\\', '/').TrimEnd('/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized[(index + 1)..] : normalized;
    }

    private static bool IsLikelyMinecraftVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
               int.TryParse(parts[0], out var major) &&
               int.TryParse(parts[1], out var minor) &&
               major == 1 &&
               minor is >= 2 and <= 50;
    }

    private sealed class MinecraftVersionComparer : IComparer<string>
    {
        public static readonly MinecraftVersionComparer Instance = new();

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
            return (version ?? "")
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out var value) ? value : 0)
                .ToArray();
        }
    }
}
