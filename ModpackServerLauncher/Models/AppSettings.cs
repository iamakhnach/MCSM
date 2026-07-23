namespace ModpackServerLauncher.Models;

public sealed class AppSettings
{
    public static string DefaultServersPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModpackServerLauncher",
        "Servers");

    public string ServersPath { get; set; } = DefaultServersPath;
    public string? JavaPath { get; set; }
    public int MaxMemoryGb { get; set; } = 8;
    public bool DarkTheme { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;
}
