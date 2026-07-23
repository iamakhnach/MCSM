using CommunityToolkit.Mvvm.ComponentModel;
using ModpackServerLauncher.Models;

namespace ModpackServerLauncher.ViewModels;

public sealed partial class ServerCardViewModel(ServerProfile profile) : ObservableObject
{
    [ObservableProperty]
    private ServerProfile _profile = profile;

    public string Name => Profile.Name;
    public string MinecraftVersion => string.IsNullOrWhiteSpace(Profile.MinecraftVersion) ? "?" : Profile.MinecraftVersion;
    public string Loader => Profile.LoaderVersion is null ? Profile.Loader.ToString() : $"{Profile.Loader} {Profile.LoaderVersion}";
    public string Memory => Profile.MemoryOption;
    public string Status => Profile.Status.ToString();
    public bool IsRunning => Profile.Status == ServerStatus.Running || Profile.Status == ServerStatus.Starting;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(MinecraftVersion));
        OnPropertyChanged(nameof(Loader));
        OnPropertyChanged(nameof(Memory));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsRunning));
    }
}
