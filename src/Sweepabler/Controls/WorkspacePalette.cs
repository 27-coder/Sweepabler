namespace ProperAppUpdater.Controls;

public static class WorkspacePalette
{
    public static string Background(WorkspaceDeck deck) => deck switch
    {
        WorkspaceDeck.Update => "#211108",
        WorkspaceDeck.Delete => "#08170F",
        WorkspaceDeck.Patch => "#09182E",
        WorkspaceDeck.Backup => "#000000",
        _ => throw new ArgumentOutOfRangeException(nameof(deck))
    };
}
