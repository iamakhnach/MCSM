namespace ModpackServerLauncher.Models;

public enum ServerStatus
{
    Ready,
    Importing,
    Installing,
    Starting,
    Running,
    Stopping,
    Stopped,
    Error
}
