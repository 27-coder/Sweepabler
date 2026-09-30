namespace ProperAppUpdater.Services;

public sealed class LocalCleanupService
{
    private static readonly TimeSpan DownloadRetention = TimeSpan.FromDays(14);
    private static readonly EnumerationOptions SafeRecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public Task<LocalCleanupResult> CleanAsync(CancellationToken cancellationToken)
    {
        return Task.Run(
            () => CleanDownloads(AppPaths.DownloadsRoot, DateTime.UtcNow, cancellationToken),
            cancellationToken);
    }

    internal static LocalCleanupResult CleanDownloads(
        string downloadsRoot,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        SafePath.RejectReparsePoints(downloadsRoot);
        Directory.CreateDirectory(downloadsRoot);
        var cutoff = utcNow - DownloadRetention;
        var deletedFiles = 0;
        long deletedBytes = 0;

        if (!Directory.Exists(downloadsRoot))
        {
            return new LocalCleanupResult(deletedFiles, deletedBytes);
        }

        // Never follow junctions or symbolic links. A lexical "inside Downloads"
        // check is not enough because a junction can point at an unrelated user folder.
        foreach (var file in Directory.EnumerateFiles(downloadsRoot, "*", SafeRecursiveEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(file);
            if (!IsInsideDownloads(downloadsRoot, info.FullName) || info.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            var length = info.Length;
            try
            {
                info.Delete();
                deletedFiles++;
                deletedBytes += length;
            }
            catch (IOException)
            {
                // A running installer may still hold the file. Try again on a later cleanup pass.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep cleanup conservative; never fight Windows permissions here.
            }
        }

        RemoveEmptyDirectories(downloadsRoot, cancellationToken);
        return new LocalCleanupResult(deletedFiles, deletedBytes);
    }

    private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SafeRecursiveEnumeration)
                     .OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsInsideDownloads(root, directory))
            {
                continue;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsInsideDownloads(string downloadsRoot, string path)
    {
        var root = Path.GetFullPath(downloadsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record LocalCleanupResult(int DeletedFiles, long DeletedBytes);
