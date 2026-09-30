using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Services;

public static partial class AppFamilyClassifier
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "application",
        "desktop",
        "installer",
        "launcher",
        "setup",
        "update",
        "updater",
        "windows",
        "win",
        "x64",
        "x86",
        "64bit",
        "32bit",
        "64",
        "32",
        "edition",
        "client"
    };

    public static void Apply(ObservableCollection<UpdateCandidateViewModel> candidates, string language)
    {
        foreach (var candidate in candidates)
        {
            candidate.IsFamilyMember = false;
            candidate.ShowFamilyHeader = false;
            candidate.FamilyTitle = string.Empty;
            candidate.FamilySortKey = string.Empty;
        }

        var familyCandidates = candidates
            .Select(candidate => new FamilyCandidate(candidate, Classify(candidate)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Family.Key))
            .ToList();

        var grouped = familyCandidates
            .GroupBy(item => item.Family.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped.Values)
        {
            var displayName = group
                .Select(item => item.Family.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name.Length)
                .First();
            var title = FormatTitle(displayName, language);

            foreach (var item in group)
            {
                item.Candidate.IsFamilyMember = true;
                item.Candidate.FamilyTitle = title;
                item.Candidate.FamilySortKey = item.Family.Key;
            }
        }

        var ordered = candidates
            .OrderBy(candidate => candidate.IsFamilyMember ? 0 : 1)
            .ThenBy(candidate => candidate.IsFamilyMember ? candidate.FamilyTitle : candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        candidates.Clear();
        foreach (var candidate in ordered)
        {
            candidates.Add(candidate);
        }

        foreach (var group in candidates
            .Where(candidate => candidate.IsFamilyMember)
            .GroupBy(candidate => candidate.FamilySortKey, StringComparer.OrdinalIgnoreCase))
        {
            group.First().ShowFamilyHeader = true;
        }
    }

    private static Family Classify(UpdateCandidateViewModel candidate)
    {
        var text = string.Join(
            ' ',
            candidate.Name,
            candidate.Publisher,
            candidate.SourceName,
            candidate.Catalog.PackageId ?? string.Empty,
            candidate.Catalog.Id,
            candidate.Catalog.Owner ?? string.Empty,
            candidate.Catalog.Repo ?? string.Empty);

        var normalized = AppMatcher.Normalize(text);
        var nameNormalized = AppMatcher.Normalize(candidate.Name);

        if (LooksLikePythonFamily(normalized))
        {
            return new Family("python", "Python");
        }

        if (LooksLikeGitFamily(nameNormalized, normalized))
        {
            return new Family("git", "Git");
        }

        if (LooksLikeJavaFamily(normalized))
        {
            return new Family("java", "Java");
        }

        if (LooksLikeNodeFamily(normalized))
        {
            return new Family("nodejs", "Node.js");
        }

        var displayName = BuildGenericDisplayName(candidate.Name);
        var key = AppMatcher.Normalize(displayName);
        return key.Length < 4 ? Family.Empty : new Family(key, displayName);
    }

    private static bool LooksLikePythonFamily(string normalized)
    {
        return ContainsAny(normalized, "python", "anaconda", "miniconda", "conda");
    }

    private static bool LooksLikeGitFamily(string nameNormalized, string normalized)
    {
        if (ContainsAny(normalized, "githubdesktop", "gitkraken", "gitlab"))
        {
            return false;
        }

        return nameNormalized is "git" or "gitbash" or "gitforwindows" ||
               ContainsAny(normalized, "gitforwindows", "gitgit", "gitbash");
    }

    private static bool LooksLikeJavaFamily(string normalized)
    {
        return ContainsAny(normalized, "java", "jdk", "jre", "temurin", "openjdk", "zulu");
    }

    private static bool LooksLikeNodeFamily(string normalized)
    {
        return ContainsAny(normalized, "nodejs", "node.js", "node", "npm", "nvm");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(AppMatcher.Normalize(needle), StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildGenericDisplayName(string name)
    {
        var cleaned = VersionLikeText().Replace(name, " ");
        cleaned = BracketedText().Replace(cleaned, " ");
        cleaned = SeparatorText().Replace(cleaned, " ");

        var tokens = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !StopWords.Contains(token))
            .Where(token => !token.Any(char.IsDigit))
            .Take(3)
            .ToList();

        if (tokens.Count == 0)
        {
            return name.Trim();
        }

        if (tokens[0].Equals("Microsoft", StringComparison.OrdinalIgnoreCase) && tokens.Count > 1)
        {
            tokens.RemoveAt(0);
        }

        if (tokens[0].Equals("AMD", StringComparison.OrdinalIgnoreCase) && tokens.Count > 2)
        {
            tokens.RemoveAt(0);
        }

        var title = string.Join(' ', tokens.Take(2));
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title);
    }

    private static string FormatTitle(string displayName, string language)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return $"{displayName}'s";
        }

        return $"{displayName}giller";
    }

    private readonly record struct Family(string Key, string DisplayName)
    {
        public static Family Empty { get; } = new(string.Empty, string.Empty);
    }

    private sealed record FamilyCandidate(UpdateCandidateViewModel Candidate, Family Family);

    [GeneratedRegex(@"\b[vV]?\d+(?:[._-]\d+){0,5}[a-zA-Z]*\b")]
    private static partial Regex VersionLikeText();

    [GeneratedRegex(@"\((?:x64|x86|64-bit|32-bit|64 bit|32 bit|win64|win32|windows)\)", RegexOptions.IgnoreCase)]
    private static partial Regex BracketedText();

    [GeneratedRegex(@"[-_/]+")]
    private static partial Regex SeparatorText();
}
