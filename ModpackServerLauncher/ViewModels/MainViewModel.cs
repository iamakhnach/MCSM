using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModpackServerLauncher.Models;
using ModpackServerLauncher.Services;

namespace ModpackServerLauncher.ViewModels;

public sealed partial class MainViewModel(
    ISettingsService settingsService,
    IServerRepository repository,
    IImportWorkflow importWorkflow,
    IJavaProvisioner javaProvisioner,
    IServerProcessService processService,
    IServerMaintenanceService maintenanceService) : ObservableObject
{
    private static readonly string[] MemoryValues = ["Auto", "2", "4", "6", "8", "12", "16"];

    [ObservableProperty]
    private ServerCardViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = "";

    [ObservableProperty]
    private string _commandText = "";

    public ObservableCollection<ServerCardViewModel> Servers { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public IReadOnlyList<string> MemoryOptions => MemoryValues;

    public event Func<string, Task<string?>>? PickArchiveRequested;
    public event Func<ServerProfile, Task<ExistingServerChoice>>? ExistingServerChoiceRequested;
    public event Func<Task<bool>>? EulaAcceptanceRequested;
    public event Func<Task<bool>>? FullCleanupConfirmationRequested;
    public event Func<SettingsViewModel, Task>? SettingsRequested;
    public event Action<string>? OpenFolderRequested;
    public event Action<string>? ErrorRequested;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await settingsService.LoadAsync();
        Servers.Clear();
        foreach (var profile in await repository.LoadAsync())
        {
            Servers.Add(new ServerCardViewModel(profile));
        }

        SelectedServer = Servers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ChooseArchiveAsync()
    {
        if (PickArchiveRequested is null)
        {
            return;
        }

        var path = await PickArchiveRequested("Выберите архив модпака");
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ImportArchiveAsync(path);
        }
    }

    public async Task ImportArchiveAsync(string archivePath)
    {
        await RunBusyAsync("Импорт сборки", async cancellationToken =>
        {
            var profile = await importWorkflow.ImportAsync(
                archivePath,
                async existing => ExistingServerChoiceRequested is null
                    ? ExistingServerChoice.Update
                    : await ExistingServerChoiceRequested(existing),
                AddLog,
                cancellationToken);

            if (profile is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingVm = Servers.FirstOrDefault(server => server.Profile.Id == profile.Id);
                if (existingVm is null)
                {
                    existingVm = new ServerCardViewModel(profile);
                    Servers.Insert(0, existingVm);
                }
                else
                {
                    existingVm.Profile = profile;
                    existingVm.Refresh();
                }

                SelectedServer = existingVm;
            });
        });
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        var vm = SelectedServer;
        await RunBusyAsync("Запуск сервера", async cancellationToken =>
        {
            var eulaPath = Path.Combine(vm.Profile.DirectoryPath, "eula.txt");
            if (!File.Exists(eulaPath) || !File.ReadAllText(eulaPath).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
            {
                var accepted = EulaAcceptanceRequested is not null && await EulaAcceptanceRequested();
                if (!accepted)
                {
                    AddLog("Запуск отменен: EULA не принята.");
                    return;
                }

                await File.WriteAllTextAsync(eulaPath, "eula=true", cancellationToken);
            }

            var javaPath = await javaProvisioner.EnsureJavaAsync(vm.Profile.MinecraftVersion, AddLog, cancellationToken);
            vm.Profile.Status = ServerStatus.Starting;
            await repository.SaveAsync(vm.Profile, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(vm.Refresh);

            var memoryMb = await ResolveMemoryMbAsync(vm.Profile, cancellationToken);
            await maintenanceService.EnsureLanAccessAsync(vm.Profile, cancellationToken);
            foreach (var address in maintenanceService.GetLanAddresses())
            {
                AddLog($"LAN: подключение с другого ПК - {address}:25565");
            }

            await processService.StartAsync(vm.Profile, javaPath, memoryMb, AddLog, cancellationToken);
            vm.Profile.Status = ServerStatus.Running;
            await repository.SaveAsync(vm.Profile, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(vm.Refresh);
        });
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        var vm = SelectedServer;
        vm.Profile.Status = ServerStatus.Stopping;
        vm.Refresh();
        await processService.StopAsync(vm.Profile, AddLog);
        vm.Profile.Status = ServerStatus.Stopped;
        await repository.SaveAsync(vm.Profile);
        vm.Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedServer is not null)
        {
            OpenFolderRequested?.Invoke(SelectedServer.Profile.DirectoryPath);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (SelectedServer is null || string.IsNullOrWhiteSpace(CommandText))
        {
            return;
        }

        var command = CommandText;
        CommandText = "";
        AddLog("> " + command);
        await processService.SendCommandAsync(SelectedServer.Profile, command);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (SettingsRequested is null)
        {
            return;
        }

        var settingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        await settingsVm.LoadAsync();
        await SettingsRequested(settingsVm);
    }

    [RelayCommand]
    private async Task FullCleanupAsync()
    {
        var confirmed = FullCleanupConfirmationRequested is not null &&
                        await FullCleanupConfirmationRequested();
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync("Полная очистка", async cancellationToken =>
        {
            await processService.StopAllAsync(AddLog, cancellationToken);
            await maintenanceService.ResetApplicationAsync(cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Servers.Clear();
                SelectedServer = null;
                Logs.Clear();
                AddLog("Приложение сброшено до состояния первого запуска.");
            });
        });
    }

    public async Task SetMemoryAsync(string memory)
    {
        if (SelectedServer is null)
        {
            return;
        }

        SelectedServer.Profile.MemoryOption = memory;
        await repository.SaveAsync(SelectedServer.Profile);
        SelectedServer.Refresh();
    }

    private async Task<int> ResolveMemoryMbAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        if (int.TryParse(profile.MemoryOption, out var gb))
        {
            return gb * 1024;
        }

        var autoGb = Math.Clamp(settings.MaxMemoryGb, 2, 16);
        return autoGb * 1024;
    }

    private async Task RunBusyAsync(string title, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = title;
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AddLog("Ошибка: " + ex.Message);
            ErrorRequested?.Invoke(ex.Message);
            if (SelectedServer is not null)
            {
                SelectedServer.Profile.Status = ServerStatus.Error;
                SelectedServer.Refresh();
            }
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private void AddLog(string line)
    {
        AppLogger.Info(line);
        Dispatcher.UIThread.Post(() =>
        {
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            while (Logs.Count > 1000)
            {
                Logs.RemoveAt(0);
            }
        });
    }
}
