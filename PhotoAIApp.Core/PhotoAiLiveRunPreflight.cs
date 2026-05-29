namespace PhotoAIApp.Core;

public sealed record PhotoAiLiveRunPreflightResult(IReadOnlyList<string> Errors)
{
    public bool CanRun => Errors.Count == 0;
}

public static class PhotoAiLiveRunPreflight
{
    public static PhotoAiLiveRunPreflightResult ValidateWritableOutputs(IEnumerable<string> selectedFolders)
    {
        List<string> errors = [];

        foreach (string folder in selectedFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ProbeWritableDirectory(folder, "selected folder", errors);
            ProbeWritableDirectory(Path.Combine(folder, ".photoai"), ".photoai log folder", errors);
        }

        return new PhotoAiLiveRunPreflightResult(errors);
    }

    private static void ProbeWritableDirectory(string directoryPath, string label, List<string> errors)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            string probePath = Path.Combine(directoryPath, $".immich-tagger-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            errors.Add($"{label} is not writable: {directoryPath} ({ex.Message})");
        }
    }
}
