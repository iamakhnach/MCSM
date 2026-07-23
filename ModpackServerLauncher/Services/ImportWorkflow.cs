using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public enum ExistingServerChoice
{
    Update,
    Replace,
    Cancel
}

public interface IImportWorkflow
{
    Task<ServerProfile?> ImportAsync(
        string archivePath,
        Func<ServerProfile, Task<ExistingServerChoice>> chooseExistingServer,
        Action<string> log,
        CancellationToken cancellationToken = default);
}

public sealed class ImportWorkflow(
    ISettingsService settingsService,
    IServerRepository repository,
    IArchiveReader archiveReader,
    IModpackAnalyzer analyzer,
    IJavaProvisioner javaProvisioner,
    IServerInstaller serverInstaller,
    HttpClient httpClient) : IImportWorkflow
{
    public async Task<ServerProfile?> ImportAsync(
        string archivePath,
        Func<ServerProfile, Task<ExistingServerChoice>> chooseExistingServer,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var isFile = File.Exists(archivePath);
        var isDirectory = Directory.Exists(archivePath);
        if (!isFile && !isDirectory)
        {
            throw new FileNotFoundException("Архив не найден.", archivePath);
        }

        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        if (isFile && extension is not ".mrpack" and not ".zip" and not ".rar" and not ".7z")
        {
            throw new InvalidOperationException("Поддерживаются только mrpack, zip, rar и 7z.");
        }

        log("Импорт: анализ архива.");
        var info = await analyzer.AnalyzeAsync(archivePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(info.MinecraftVersion))
        {
            throw new InvalidOperationException("Не удалось определить версию Minecraft. Импорт остановлен.");
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        var existing = await repository.FindByNameAsync(info.Name, cancellationToken);
        if (existing is not null)
        {
            var choice = await chooseExistingServer(existing);
            if (choice == ExistingServerChoice.Cancel)
            {
                log("Импорт отменен.");
                return null;
            }

            if (choice == ExistingServerChoice.Replace && Directory.Exists(existing.DirectoryPath))
            {
                Directory.Delete(existing.DirectoryPath, recursive: true);
            }
        }

        var serverDirectory = existing?.DirectoryPath ?? Path.Combine(settings.ServersPath, info.Name);
        Directory.CreateDirectory(serverDirectory);

        var profile = existing ?? new ServerProfile
        {
            Name = info.Name,
            DirectoryPath = serverDirectory
        };

        profile.MinecraftVersion = info.MinecraftVersion;
        profile.Loader = info.Loader == LoaderType.Unknown ? LoaderType.Vanilla : info.Loader;
        profile.LoaderVersion = info.LoaderVersion;
        profile.ServerJarPath = Path.Combine(serverDirectory, "server.jar");
        profile.Status = ServerStatus.Importing;

        log("Импорт: распаковка серверных папок.");
        await archiveReader.ExtractServerFilesAsync(archivePath, serverDirectory, cancellationToken);

        if (info.IsMrPack && info.MrPackFiles.Count > 0)
        {
            await DownloadMrPackServerFilesAsync(info, serverDirectory, log, cancellationToken);
        }

        profile.Status = ServerStatus.Installing;
        var javaPath = await javaProvisioner.EnsureJavaAsync(info.MinecraftVersion, log, cancellationToken);
        await serverInstaller.InstallAsync(profile, javaPath, log, cancellationToken);

        profile.Status = ServerStatus.Ready;
        await repository.SaveAsync(profile, cancellationToken);
        log("Готово: сервер импортирован.");
        return profile;
    }

    private async Task DownloadMrPackServerFilesAsync(
        ModpackInfo info,
        string serverDirectory,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        log("Modrinth: скачивание серверных файлов сборки.");
        foreach (var file in info.MrPackFiles)
        {
            if (string.IsNullOrWhiteSpace(file.PrimaryDownloadUrl) || string.IsNullOrWhiteSpace(file.Path))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(serverDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(Path.GetFullPath(serverDirectory), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var response = await httpClient.GetAsync(file.PrimaryDownloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var local = File.Create(destination);
            await remote.CopyToAsync(local, cancellationToken);
        }
    }
}
