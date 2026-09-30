using System.Collections.ObjectModel;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Tests;

internal static class AppFamilyTests
{
    public static Task PythonVariantsAsync()
    {
        var pythonApps = new[]
        {
            Candidate("Python 3.13 (64-bit)", "winget", "Python.Python.3.13", "Python Software Foundation", "3.13.0"),
            Candidate("CPython runtime", "winget", "Python.Python.3.12", "", "3.12.0"),
            Candidate("Anaconda3", "choco", "anaconda3", "Anaconda, Inc.", "2025.6"),
            Candidate("Miniconda3", "scoop", "extras/miniconda3", "Anaconda, Inc.", "25.1"),
            Candidate("Runtime toolkit", "github", "runtime-toolkit", "Python Software Foundation", "3.11.0")
        };
        pythonApps[2].IsSelected = false;
        var other = Candidate("KeePassXC", "winget", "KeePassXCTeam.KeePassXC", "KeePassXC Team", "2.7.0");
        var candidates = new ObservableCollection<UpdateCandidateViewModel>(new[] { other }.Concat(pythonApps.Reverse()));
        var original = pythonApps.ToDictionary(app => app,
            app => (app.Name, app.InstalledVersion, app.Catalog.Provider, app.Catalog.PackageId, app.SourceName, app.IsSelected));

        AppFamilyClassifier.Apply(candidates, "en");

        Check(pythonApps.All(app => app.IsFamilyMember && app.FamilyTitle == "Python's"),
            "Python versions, distributions, package identity, or publisher identity did not join the Python family");
        Check(candidates.Count == 6 && candidates.Take(5).All(app => pythonApps.Contains(app)),
            "grouping lost an app or separated related rows");
        Check(pythonApps.Count(app => app.ShowFamilyHeader) == 1 && candidates[0].ShowFamilyHeader,
            "Python family has a missing or repeated header");
        Check(!other.IsFamilyMember && !other.ShowFamilyHeader,
            "an unrelated app joined the family just because it also uses WinGet");
        foreach (var app in pythonApps)
        {
            Check(original[app] == (app.Name, app.InstalledVersion, app.Catalog.Provider, app.Catalog.PackageId, app.SourceName, app.IsSelected),
                "grouping changed an app's identity, source, version, or selected state");
        }

        AppFamilyClassifier.Apply(candidates, "tr");
        Check(pythonApps.All(app => app.FamilyTitle == "Pythongiller") && pythonApps.Count(app => app.ShowFamilyHeader) == 1,
            "changing language left stale or repeated family headers");
        return Task.CompletedTask;
    }

    public static Task HeaderAfterRemovalAsync()
    {
        var candidates = new ObservableCollection<UpdateCandidateViewModel>
        {
            Candidate("Python 3.13", "winget", "Python.Python.3.13", "Python Software Foundation", "3.13.0"),
            Candidate("Anaconda3", "choco", "anaconda3", "Anaconda, Inc.", "2025.6"),
            Candidate("Miniconda3", "scoop", "miniconda3", "Anaconda, Inc.", "25.1")
        };
        AppFamilyClassifier.Apply(candidates, "en");
        candidates.Remove(candidates.Single(app => app.ShowFamilyHeader));
        AppFamilyClassifier.Apply(candidates, "en");
        Check(candidates.Count(app => app.ShowFamilyHeader) == 1 && candidates[0].ShowFamilyHeader,
            "removing an updated app did not move the header to the remaining family");

        candidates.RemoveAt(0);
        AppFamilyClassifier.Apply(candidates, "en");
        Check(!candidates[0].IsFamilyMember && !candidates[0].ShowFamilyHeader && candidates[0].FamilyTitle.Length == 0,
            "a lone remaining app kept a stale family header");
        candidates.Clear();
        AppFamilyClassifier.Apply(candidates, "en");
        return Task.CompletedTask;
    }

    private static UpdateCandidateViewModel Candidate(string name, string provider, string packageId, string publisher, string version) =>
        new(new CatalogEntry { Id = packageId, Name = name, Provider = provider, PackageId = packageId },
            new InstalledApp { DisplayName = name, DisplayVersion = version, Publisher = publisher })
        {
            SourceName = provider,
            LatestVersion = version + ".1",
            UpdateAvailable = true,
            IsSelected = true
        };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
