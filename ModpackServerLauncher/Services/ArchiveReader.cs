using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ModpackServerLauncher.Services;

public sealed record ArchiveEntryInfo(string Key, bool IsDirectory);

public interface IArchiveReader
{
    Task<IReadOnlyList<ArchiveEntryInfo>> ListEntriesAsync(string archivePath, CancellationToken cancellationToken = default);
    Task<string?> ReadTextAsync(string archivePath, string entryKey, CancellationToken cancellationToken = default);
    Task ExtractServerFilesAsync(string archivePath, string destination, CancellationToken cancellationToken = default);
}

public sealed class SharpCompressArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> ServerFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mods",
        "config",
        "defaultconfigs",
        "kubejs",
        "scripts",
        "resourcepacks",
        "datapacks",
        "world",
        "saves",
        "serverconfig",
        "libraries",
        "patchouli_books"
    };

    public Task<IReadOnlyList<ArchiveEntryInfo>> ListEntriesAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(archivePath))
        {
            return Task.FromResult(ListDirectoryEntries(archivePath, cancellationToken));
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        IReadOnlyList<ArchiveEntryInfo> entries = archive.Entries
            .Select(entry => new ArchiveEntryInfo(Normalize(entry.Key), entry.IsDirectory))
            .ToArray();
        return Task.FromResult(entries);
    }

    public async Task<string?> ReadTextAsync(
        string archivePath,
        string entryKey,
        CancellationToken cancellationToken = default)
    {
        await using var stream = Directory.Exists(archivePath)
            ? OpenDirectoryEntryStream(archivePath, entryKey)
            : OpenEntryStream(archivePath, entryKey);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public Task ExtractServerFilesAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destination);
        if (Directory.Exists(archivePath))
        {
            ExtractDirectoryServerFiles(archivePath, destination, cancellationToken);
            return Task.CompletedTask;
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetRelativePath = GetServerRelativePath(Normalize(entry.Key));
            if (targetRelativePath is null)
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destination, targetRelativePath));
            if (!targetPath.StartsWith(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.WriteToFile(targetPath, new ExtractionOptions
            {
                ExtractFullPath = false,
                Overwrite = true
            });
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<ArchiveEntryInfo> ListDirectoryEntries(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(directoryPath);
        var entries = new List<ArchiveEntryInfo>();

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new ArchiveEntryInfo(Normalize(Path.GetRelativePath(root, directory)), IsDirectory: true));
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new ArchiveEntryInfo(Normalize(Path.GetRelativePath(root, file)), IsDirectory: false));
        }

        return entries;
    }

    private static Stream? OpenDirectoryEntryStream(string directoryPath, string entryKey)
    {
        var root = Path.GetFullPath(directoryPath);
        var target = Path.GetFullPath(Path.Combine(root, Normalize(entryKey).Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinDirectory(target, root) || !File.Exists(target))
        {
            return null;
        }

        return File.OpenRead(target);
    }

    private static void ExtractDirectoryServerFiles(
        string directoryPath,
        string destination,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(directoryPath);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Normalize(Path.GetRelativePath(root, file));
            var targetRelativePath = GetServerRelativePath(key);
            if (targetRelativePath is null)
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destination, targetRelativePath));
            if (!IsWithinDirectory(targetPath, destination))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static Stream? OpenEntryStream(string archivePath, string entryKey)
    {
        var normalizedKey = Normalize(entryKey);
        var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        var entry = archive.Entries.FirstOrDefault(candidate =>
            !candidate.IsDirectory &&
            string.Equals(Normalize(candidate.Key), normalizedKey, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            archive.Dispose();
            return null;
        }

        var memory = new MemoryStream();
        using (var source = entry.OpenEntryStream())
        {
            source.CopyTo(memory);
        }

        archive.Dispose();
        memory.Position = 0;
        return memory;
    }

    private static string? GetServerRelativePath(string key)
    {
        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts[0].Equals("overrides", StringComparison.OrdinalIgnoreCase) ||
            parts[0].Equals("server-overrides", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length > 1 ? Path.Combine(parts.Skip(1).ToArray()) : null;
        }

        for (var index = 0; index < parts.Length; index++)
        {
            if (!ServerFolderNames.Contains(parts[index]))
            {
                continue;
            }

            var relativeParts = parts.Skip(index).ToArray();
            if (relativeParts[0].Equals("saves", StringComparison.OrdinalIgnoreCase))
            {
                relativeParts[0] = "world";
            }

            return Path.Combine(relativeParts);
        }

        if (parts.Length == 1 && IsServerRootFile(parts[0]))
        {
            return parts[0];
        }

        return null;
    }

    private static bool IsServerRootFile(string fileName)
    {
        return fileName.Equals("server.properties", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("ops.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("whitelist.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("banned-players.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("banned-ips.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? path)
    {
        return (path ?? "").Replace('\\', '/').TrimStart('/');
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }
}
