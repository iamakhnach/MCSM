using System.Diagnostics;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.Services;

public interface IServerProcessService
{
    bool IsRunning(ServerProfile profile);
    Task StartAsync(ServerProfile profile, string javaPath, int memoryMb, Action<string> log, CancellationToken cancellationToken = default);
    Task StopAsync(ServerProfile profile, Action<string> log, CancellationToken cancellationToken = default);
    Task StopAllAsync(Action<string> log, CancellationToken cancellationToken = default);
    Task SendCommandAsync(ServerProfile profile, string command, CancellationToken cancellationToken = default);
}

public sealed class ServerProcessService : IServerProcessService
{
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning(ServerProfile profile)
    {
        return _processes.TryGetValue(profile.Id, out var process) && !process.HasExited;
    }

    public Task StartAsync(
        ServerProfile profile,
        string javaPath,
        int memoryMb,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning(profile))
        {
            return Task.CompletedTask;
        }

        RepairLaunchTarget(profile, log);
        var arguments = BuildArguments(profile, memoryMb);
        var startInfo = new ProcessStartInfo(javaPath, arguments)
        {
            WorkingDirectory = profile.DirectoryPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить сервер.");

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                log(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                log(args.Data);
            }
        };
        process.Exited += (_, _) => log("Сервер остановлен.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes[profile.Id] = process;
        return Task.CompletedTask;
    }

    public async Task StopAsync(ServerProfile profile, Action<string> log, CancellationToken cancellationToken = default)
    {
        if (!_processes.TryGetValue(profile.Id, out var process) || process.HasExited)
        {
            return;
        }

        await process.StandardInput.WriteLineAsync("stop".AsMemory(), cancellationToken);
        var delay = Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        var exited = await Task.WhenAny(process.WaitForExitAsync(cancellationToken), delay);
        if (exited == delay && !process.HasExited)
        {
            log("Сервер не остановился штатно, процесс будет завершен.");
            process.Kill(entireProcessTree: true);
        }
    }

    public async Task StopAllAsync(Action<string> log, CancellationToken cancellationToken = default)
    {
        foreach (var process in _processes.Values.ToArray())
        {
            if (process.HasExited)
            {
                continue;
            }

            try
            {
                await process.StandardInput.WriteLineAsync("stop".AsMemory(), cancellationToken);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        foreach (var process in _processes.Values.ToArray())
        {
            if (!process.HasExited)
            {
                log("Сервер все еще работает, процесс будет завершен.");
                process.Kill(entireProcessTree: true);
            }
        }

        _processes.Clear();
    }

    public async Task SendCommandAsync(ServerProfile profile, string command, CancellationToken cancellationToken = default)
    {
        if (!_processes.TryGetValue(profile.Id, out var process) || process.HasExited)
        {
            return;
        }

        await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
    }

    private static string BuildArguments(ServerProfile profile, int memoryMb)
    {
        var xms = Math.Min(1024, Math.Max(512, memoryMb / 2));
        if (!string.IsNullOrWhiteSpace(profile.ArgumentsFilePath) && File.Exists(profile.ArgumentsFilePath))
        {
            return $"-Xms{xms}M -Xmx{memoryMb}M @\"{profile.ArgumentsFilePath}\" nogui";
        }

        return $"-Xms{xms}M -Xmx{memoryMb}M -jar \"{profile.ServerJarPath}\" nogui";
    }

    private static void RepairLaunchTarget(ServerProfile profile, Action<string> log)
    {
        if (!string.IsNullOrWhiteSpace(profile.ArgumentsFilePath) && File.Exists(profile.ArgumentsFilePath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.ServerJarPath) && File.Exists(profile.ServerJarPath))
        {
            return;
        }

        var repairedJar = FindLaunchJar(profile);
        if (repairedJar is null)
        {
            return;
        }

        profile.ServerJarPath = repairedJar;
        log($"Launch: using {Path.GetFileName(repairedJar)}.");
    }

    private static string? FindLaunchJar(ServerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.DirectoryPath) || !Directory.Exists(profile.DirectoryPath))
        {
            return null;
        }

        var jars = Directory.EnumerateFiles(profile.DirectoryPath, "*.jar", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Contains("installer", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var loaderMarker = profile.Loader switch
        {
            LoaderType.Forge => "forge",
            LoaderType.NeoForge => "neoforge",
            LoaderType.Fabric => "fabric",
            LoaderType.Quilt => "quilt",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(loaderMarker))
        {
            var loaderJar = jars.FirstOrDefault(path =>
                Path.GetFileName(path).Contains(loaderMarker, StringComparison.OrdinalIgnoreCase));
            if (loaderJar is not null)
            {
                return loaderJar;
            }
        }

        return jars.FirstOrDefault(path =>
                   Path.GetFileName(path).Contains("server", StringComparison.OrdinalIgnoreCase)) ??
               jars.FirstOrDefault();
    }
}
