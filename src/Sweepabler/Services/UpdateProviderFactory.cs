using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class UpdateProviderFactory
{
    private readonly GitHubReleaseProvider _githubReleaseProvider;
    private readonly ElectronYamlReleaseProvider _electronYamlReleaseProvider;
    private readonly OfficialWebPageProvider _officialWebPageProvider;
    private readonly AppcastReleaseProvider _appcastReleaseProvider;

    public UpdateProviderFactory(
        GitHubReleaseProvider githubReleaseProvider,
        ElectronYamlReleaseProvider electronYamlReleaseProvider,
        OfficialWebPageProvider officialWebPageProvider,
        AppcastReleaseProvider appcastReleaseProvider)
    {
        _githubReleaseProvider = githubReleaseProvider;
        _electronYamlReleaseProvider = electronYamlReleaseProvider;
        _officialWebPageProvider = officialWebPageProvider;
        _appcastReleaseProvider = appcastReleaseProvider;
    }

    public IUpdateProvider Resolve(CatalogEntry catalog)
    {
        return catalog.Provider.ToLowerInvariant() switch
        {
            "github" => _githubReleaseProvider,
            "electron-yml" => _electronYamlReleaseProvider,
            "official-web" => _officialWebPageProvider,
            "web-page" => _officialWebPageProvider,
            "appcast" => _appcastReleaseProvider,
            _ => throw new NotSupportedException($"Provider '{catalog.Provider}' is not supported yet.")
        };
    }
}
