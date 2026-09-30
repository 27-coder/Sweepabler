using System.Text.RegularExpressions;

namespace ProperAppUpdater.Services;

internal static partial class PackageIdentity
{
    public static string Require(string provider, string id)
    {
        var parts = provider.Equals("scoop", StringComparison.OrdinalIgnoreCase) ? id.Split('/') : new[] { id };
        if (parts.Length is < 1 or > 2 || parts.Any(part => !Identifier().IsMatch(part) || part is "." or ".." || part.EndsWith('.')))
            throw new InvalidOperationException("Invalid package identity.");
        return id;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
