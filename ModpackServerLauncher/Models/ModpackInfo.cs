namespace ModpackServerLauncher.Models;

public sealed class ModpackInfo
{
    public string Name { get; set; } = "Minecraft Server";
    public string MinecraftVersion { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Unknown;
    public string? LoaderVersion { get; set; }
    public bool IsMrPack { get; set; }
    public IReadOnlyList<MrPackFile> MrPackFiles { get; set; } = Array.Empty<MrPackFile>();
}

public sealed class MrPackFile
{
    public string Path { get; set; } = "";
    public string? PrimaryDownloadUrl { get; set; }
    public bool ServerRequired { get; set; }
}
