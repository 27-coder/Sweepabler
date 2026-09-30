namespace ProperAppUpdater.Services;

internal static class SafePath
{
    public static string RequireInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationService.Current.Get("UnsafeDownloadPath"));
        RejectReparsePoints(fullRoot);
        RejectReparsePoints(fullPath);
        return fullPath;
    }

    public static void RejectReparsePoints(string path)
    {
        var full = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(full))
        {
            try
            {
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(LocalizationService.Current.Get("UnsafeLinkedPath"));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            full = Path.GetDirectoryName(full);
        }
    }

    public static string FileSegment(string value)
    {
        var cleaned = new string(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.', ' ');
        if (cleaned is "" or "." or "..") return string.Empty;
        var stem = cleaned.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789".Contains(stem[3])))
            cleaned = "_" + cleaned;
        return cleaned;
    }
}
