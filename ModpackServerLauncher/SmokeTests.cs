using System.IO.Compression;
using ModpackServerLauncher.Models;
using ModpackServerLauncher.Services;

namespace ModpackServerLauncher;

internal static class SmokeTests
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "ModpackServerLauncherSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "TestPack.mrpack");
        var extractPath = Path.Combine(root, "server");

        try
        {
            CreateMrPack(archivePath);

            IArchiveReader archiveReader = new SharpCompressArchiveReader();
            IModpackAnalyzer analyzer = new ModpackAnalyzer(archiveReader);
            var info = await analyzer.AnalyzeAsync(archivePath);

            Assert(info.Name == "Test Pack", "Name was not parsed.");
            Assert(info.MinecraftVersion == "1.20.1", "Minecraft version was not parsed.");
            Assert(info.Loader == LoaderType.Fabric, "Loader was not parsed.");
            Assert(info.LoaderVersion == "0.15.11", "Loader version was not parsed.");

            await archiveReader.ExtractServerFilesAsync(archivePath, extractPath);
            Assert(File.Exists(Path.Combine(extractPath, "config", "server.properties")), "Override file was not extracted.");

            var rawArchivePath = Path.Combine(root, "Path To Space v7.1.zip");
            CreateRawForgePack(rawArchivePath);
            var rawInfo = await analyzer.AnalyzeAsync(rawArchivePath);

            Assert(rawInfo.MinecraftVersion == "1.20.1", "Minecraft version was not inferred from raw mod files.");
            Assert(rawInfo.Loader == LoaderType.Forge, "Forge loader was not inferred from raw mod files.");

            var serverLikeDirectory = Path.Combine(root, "PathToSpaceServer");
            Directory.CreateDirectory(serverLikeDirectory);
            File.WriteAllText(Path.Combine(serverLikeDirectory, "forge-1.20.1-47.4.22-installer.jar"), "");
            Assert(FindServerJarForTest(serverLikeDirectory, "forge") is null, "Forge installer was detected as a server jar.");

            var rawDirectoryPath = Path.Combine(root, ".Path To Space v7.1");
            CreateRawForgePackDirectory(rawDirectoryPath);
            var directoryInfo = await analyzer.AnalyzeAsync(rawDirectoryPath);
            Assert(directoryInfo.Name == "Path To Space v7.1", "Directory modpack name was not cleaned.");
            Assert(directoryInfo.MinecraftVersion == "1.20.1", "Minecraft version was not inferred from raw modpack directory.");
            Assert(directoryInfo.Loader == LoaderType.Forge, "Forge loader was not inferred from raw modpack directory.");

            var directoryExtractPath = Path.Combine(root, "directory-server");
            await archiveReader.ExtractServerFilesAsync(rawDirectoryPath, directoryExtractPath);
            Assert(File.Exists(Path.Combine(directoryExtractPath, "mods", "ad_astra-forge-1.20.1-1.15.19.jar")), "Directory mod file was not extracted.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateMrPack(string archivePath)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var index = archive.CreateEntry("modrinth.index.json");
        using (var writer = new StreamWriter(index.Open()))
        {
            writer.Write("""
                {
                  "formatVersion": 1,
                  "game": "minecraft",
                  "versionId": "1.0.0",
                  "name": "Test Pack",
                  "dependencies": {
                    "minecraft": "1.20.1",
                    "fabric-loader": "0.15.11"
                  },
                  "files": []
                }
                """);
        }

        var config = archive.CreateEntry("overrides/config/server.properties");
        using var configWriter = new StreamWriter(config.Open());
        configWriter.Write("motd=Smoke");
    }

    private static void CreateRawForgePack(string archivePath)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        archive.CreateEntry("Path To Space v7.1/mods/ad_astra-forge-1.20.1-1.15.19.jar");
        archive.CreateEntry("Path To Space v7.1/mods/create-1.20.1-0.5.1.j.jar");
        archive.CreateEntry("Path To Space v7.1/mods/jei-1.20.1-forge-15.20.0.106.jar");
        archive.CreateEntry("Path To Space v7.1/mods/kotlinforforge-4.11.0-all.jar");
        archive.CreateEntry("Path To Space v7.1/config/example.toml");
    }

    private static void CreateRawForgePackDirectory(string directoryPath)
    {
        Directory.CreateDirectory(Path.Combine(directoryPath, "mods"));
        Directory.CreateDirectory(Path.Combine(directoryPath, "config"));
        File.WriteAllText(Path.Combine(directoryPath, "mods", "ad_astra-forge-1.20.1-1.15.19.jar"), "");
        File.WriteAllText(Path.Combine(directoryPath, "mods", "jei-1.20.1-forge-15.20.0.106.jar"), "");
        File.WriteAllText(Path.Combine(directoryPath, "config", "example.toml"), "");
    }

    private static string? FindServerJarForTest(string directory, string marker)
    {
        var method = typeof(ServerInstaller).GetMethod(
            "FindServerJar",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (string?)method?.Invoke(null, [directory, marker]);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
