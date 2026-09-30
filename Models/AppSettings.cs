namespace ProperAppUpdater.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = string.Empty;
    public int SetupVersion { get; set; }
    public string UpdateRepository { get; set; } = string.Empty;
    public DateTimeOffset? LastMaintenanceUtc { get; set; }
}
