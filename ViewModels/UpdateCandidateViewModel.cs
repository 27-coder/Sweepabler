using ProperAppUpdater.Models;
using ProperAppUpdater.Services;

namespace ProperAppUpdater.ViewModels;

public sealed class UpdateCandidateViewModel : ObservableObject
{
    private bool _isSelected;
    private string _latestVersion = string.Empty;
    private string _status = "Installed";
    private bool _updateAvailable;
    private string _downloadUrl = string.Empty;
    private string _sha512 = string.Empty;
    private string _sha256 = string.Empty;
    private string _sourceName = string.Empty;
    private string _trustSummary = string.Empty;
    private string _familyTitle = string.Empty;
    private string _familySortKey = string.Empty;
    private bool _isFamilyMember;
    private bool _showFamilyHeader;
    private bool _isSelfUpdate;

    public UpdateCandidateViewModel(CatalogEntry catalog, InstalledApp installedApp)
    {
        Catalog = catalog;
        Name = catalog.Name;
        InstalledVersion = installedApp.DisplayVersion;
        Publisher = installedApp.Publisher;
        InstallLocation = installedApp.InstallLocation;
        DisplayIcon = installedApp.DisplayIcon;
        IconSource = AppIconService.ResolveIconSource(installedApp.DisplayIcon);
        UninstallString = installedApp.UninstallString;
        SourceName = catalog.Provider;
    }

    public long? DownloadSizeBytes { get; set; }
    public CatalogEntry Catalog { get; }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(PirateMenuText));
        OnPropertyChanged(nameof(DeleteMenuText));
        OnPropertyChanged(nameof(FilePathMenuText));
    }
    public string Name { get; }
    public string InstalledVersion { get; private set; }
    public string Publisher { get; }
    public string InstallLocation { get; }
    public string DisplayIcon { get; }
    public string IconSource { get; }
    public string UninstallString { get; }
    public string PirateMenuText => LocalizationService.Current.Get("PirateCandidateMenu");
    public string DeleteMenuText => LocalizationService.Current.Get("DeleteCandidateMenu");
    public string FilePathMenuText => LocalizationService.Current.Get("FilePathCandidateMenu");

    public string VersionLine => string.IsNullOrWhiteSpace(LatestVersion)
        ? InstalledVersion
        : $"{InstalledVersion} -> {LatestVersion}";

    public string SourceLine
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TrustSummary))
            {
                return SourceName;
            }

            return $"{SourceName} - {TrustSummary}";
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string LatestVersion
    {
        get => _latestVersion;
        set
        {
            if (SetProperty(ref _latestVersion, value))
            {
                OnPropertyChanged(nameof(VersionLine));
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set => SetProperty(ref _updateAvailable, value);
    }

    public string DownloadUrl
    {
        get => _downloadUrl;
        set => SetProperty(ref _downloadUrl, value);
    }

    public string Sha512
    {
        get => _sha512;
        set => SetProperty(ref _sha512, value);
    }

    public string Sha256
    {
        get => _sha256;
        set => SetProperty(ref _sha256, value);
    }

    public string SourceName
    {
        get => _sourceName;
        set
        {
            if (SetProperty(ref _sourceName, value))
            {
                OnPropertyChanged(nameof(SourceLine));
            }
        }
    }

    public string TrustSummary
    {
        get => _trustSummary;
        set
        {
            if (SetProperty(ref _trustSummary, value))
            {
                OnPropertyChanged(nameof(SourceLine));
            }
        }
    }

    public string FamilyTitle
    {
        get => _familyTitle;
        set => SetProperty(ref _familyTitle, value);
    }

    public string FamilySortKey
    {
        get => _familySortKey;
        set => SetProperty(ref _familySortKey, value);
    }

    public bool IsFamilyMember
    {
        get => _isFamilyMember;
        set => SetProperty(ref _isFamilyMember, value);
    }

    public bool ShowFamilyHeader
    {
        get => _showFamilyHeader;
        set => SetProperty(ref _showFamilyHeader, value);
    }

    public bool IsSelfUpdate
    {
        get => _isSelfUpdate;
        set => SetProperty(ref _isSelfUpdate, value);
    }

    public void MarkInstalledVersion(string version)
    {
        InstalledVersion = version;
        OnPropertyChanged(nameof(InstalledVersion));
        OnPropertyChanged(nameof(VersionLine));
    }

    public override string ToString()
    {
        return Name;
    }
}
