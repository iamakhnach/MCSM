namespace ModpackServerLauncher.Models;

public sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Minecraft Server";
    public string MinecraftVersion { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Unknown;
    public string? LoaderVersion { get; set; }
    public string DirectoryPath { get; set; } = "";
    public string ServerJarPath { get; set; } = "";
    public string? ArgumentsFilePath { get; set; }
    public string MemoryOption { get; set; } = "Auto";
    public ServerStatus Status { get; set; } = ServerStatus.Ready;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
