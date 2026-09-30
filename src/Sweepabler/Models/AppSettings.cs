namespace ProperAppUpdater.Models;

public sealed class AppSettings
{
    public const string DefaultUpdateRepository = "27-coder/Sweepabler";
    private string _updateRepository = DefaultUpdateRepository;

    public string Language { get; set; } = string.Empty;
    public int SetupVersion { get; set; }
    public string UpdateRepository
    {
        get => _updateRepository;
        set => _updateRepository = string.IsNullOrWhiteSpace(value) ? DefaultUpdateRepository : value.Trim();
    }
    public DateTimeOffset? LastMaintenanceUtc { get; set; }
}
