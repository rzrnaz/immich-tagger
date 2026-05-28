namespace PhotoAIApp.Core;

public static class PhotoAiFolderSelection
{
    public static string[] NormalizeAndValidateSelectedFolders(IEnumerable<string?> selectedFolders, string? safetyRootPath = null)
    {
        string? normalizedSafetyRoot = string.IsNullOrWhiteSpace(safetyRootPath)
            ? null
            : NormalizeDirectoryPath(safetyRootPath);
        if (normalizedSafetyRoot is not null && !Directory.Exists(normalizedSafetyRoot))
        {
            throw new DirectoryNotFoundException($"Safety root does not exist: {normalizedSafetyRoot}");
        }

        var normalizedFolders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? selectedFolder in selectedFolders)
        {
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                continue;
            }

            string normalizedFolder = NormalizeDirectoryPath(selectedFolder);
            if (!seen.Add(normalizedFolder))
            {
                continue;
            }

            normalizedFolders.Add(normalizedFolder);
        }

        if (normalizedFolders.Count < 1)
        {
            throw new InvalidOperationException("Select at least one folder to scan.");
        }

        foreach (string folder in normalizedFolders)
        {
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException($"Selected folder does not exist: {folder}");
            }

            if (IsExcludedScanFolder(folder))
            {
                throw new InvalidOperationException($"Selected folder is an internal/system folder and cannot be scanned: {folder}");
            }

            if (normalizedSafetyRoot is not null && !PhotoAiScanner.IsPathUnderRoot(folder, normalizedSafetyRoot))
            {
                throw new InvalidOperationException($"Selected folder must be inside the configured safety root: {folder}");
            }
        }

        return normalizedFolders.ToArray();
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsExcludedScanFolder(string folderPath)
    {
        string[] parts = NormalizeDirectoryPath(folderPath).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(part =>
            string.Equals(part, ".photoai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, ".Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "@eaDir", StringComparison.OrdinalIgnoreCase));
    }
}
