using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public interface IUpdateProvider
{
    Task<ReleaseInfo> GetLatestAsync(CatalogEntry catalog, CancellationToken cancellationToken);
}
