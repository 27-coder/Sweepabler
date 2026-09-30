namespace ProperAppUpdater.ViewModels;

public sealed class IgnoreAppViewModel : ObservableObject
{
    private bool _isSelected;

    public string DisplayName { get; init; } = string.Empty;
    public string DisplayVersion { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;

    public string DetailLine
    {
        get
        {
            var parts = new[] { DisplayVersion, Publisher }
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" - ", parts);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
