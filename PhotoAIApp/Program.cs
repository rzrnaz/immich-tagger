using PhotoAIApp.Core;
using System.Text.Json;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
var scanner = new PhotoAiScanner();

try
{
    if (command == "analyze")
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Missing image path.");
            Console.WriteLine();
            PrintUsage();
            return 1;
        }

        string imagePath = args[1];
        string rawResponse = await scanner.AnalyzeImageAsync(imagePath, PhotoAiDefaults.OllamaBaseUrl, PhotoAiDefaults.Model);
        PhotoAnalysis? analysis = PhotoAiScanner.TryParsePhotoAnalysis(rawResponse);

        Console.WriteLine();
        Console.WriteLine("Raw model response:");
        Console.WriteLine("-------------------");
        Console.WriteLine(rawResponse);

        Console.WriteLine();
        Console.WriteLine("Parsed analysis:");
        Console.WriteLine("----------------");
        Console.WriteLine(JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true }));

        return 0;
    }

    if (command == "scan")
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Missing folder path.");
            Console.WriteLine();
            PrintUsage();
            return 1;
        }

        string folderPath = args[1];

        bool recursive = args.Any(a => string.Equals(a, "--recursive", StringComparison.OrdinalIgnoreCase));
        bool force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
        bool writeJson = true;
        bool writeXmp = args.Any(a => string.Equals(a, "--write-xmp", StringComparison.OrdinalIgnoreCase));
        bool overwriteJson = args.Any(a => string.Equals(a, "--overwrite-json", StringComparison.OrdinalIgnoreCase));
        bool overwriteXmp = args.Any(a => string.Equals(a, "--overwrite-xmp", StringComparison.OrdinalIgnoreCase));
        bool addTags = args.Any(a => string.Equals(a, "--add-tags", StringComparison.OrdinalIgnoreCase));
        bool dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
        int? limit = ReadIntOption(args, "--limit");
        string? safetyRoot = ReadStringOption(args, "--safety-root");

        var progress = new Progress<PhotoAiScanProgress>(item => Console.WriteLine(item.Message));

        PhotoAiScanSummary summary = await scanner.ScanFolderAsync(
            new PhotoAiScanOptions
            {
                FolderPath = folderPath,
                SafetyRootPath = safetyRoot,
                Recursive = recursive,
                Force = force,
                WriteJson = writeJson,
                WriteXmp = writeXmp,
                OverwriteJson = overwriteJson,
                OverwriteXmp = overwriteXmp,
                AddTags = addTags,
                DryRun = dryRun,
                Limit = limit
            },
            progress
        );

        Console.WriteLine();
        Console.WriteLine("Scan complete.");
        Console.WriteLine($"Images found:        {summary.ImagesFound}");
        if (summary.DryRun)
        {
            Console.WriteLine("Mode:                DRY RUN / PREVIEW");
            Console.WriteLine($"Would process:       {summary.WouldProcess}");
            Console.WriteLine($"Would write JSON:    {summary.WouldWriteJsonSidecar}");
            Console.WriteLine($"Would write XMP:     {summary.WouldWriteXmpSidecar}");
            Console.WriteLine($"Would skip JSON:     {summary.WouldSkipJsonSidecar}");
            Console.WriteLine($"Would skip XMP:      {summary.WouldSkipXmpSidecar}");
            Console.WriteLine($"Existing JSON:       {summary.ExistingJsonSidecars}");
            Console.WriteLine($"Existing XMP:        {summary.ExistingXmpSidecars}");
            Console.WriteLine($"Would skip:          {summary.Skipped}");
            Console.WriteLine($"Would block/fail:    {summary.Failed}");
        }
        else
        {
            Console.WriteLine($"Completed:           {summary.Completed}");
            Console.WriteLine($"Skipped:             {summary.Skipped}");
            Console.WriteLine($"Failed:              {summary.Failed}");
            Console.WriteLine($"Parse/XMP skipped:   {summary.ParseFailed}");
            Console.WriteLine($"XMP written:         {summary.XmpWritten}");
            Console.WriteLine($"JSON write skipped:  {summary.JsonWriteSkipped}");
            Console.WriteLine($"XMP write skipped:   {summary.XmpWriteSkipped}");
            Console.WriteLine($"Model failures:      {summary.ModelFailures}");
            Console.WriteLine($"Fallback attempts:   {summary.FallbackAttempts}");
            Console.WriteLine($"Fallback successes:  {summary.FallbackSucceeded}");
            Console.WriteLine($"Anomaly log:         {summary.RunLogPath}");
        }

        return summary.Failed == 0 ? 0 : 1;
    }

    if (File.Exists(args[0]))
    {
        string rawResponse = await scanner.AnalyzeImageAsync(args[0], PhotoAiDefaults.OllamaBaseUrl, PhotoAiDefaults.Model);
        PhotoAnalysis? analysis = PhotoAiScanner.TryParsePhotoAnalysis(rawResponse);

        Console.WriteLine();
        Console.WriteLine("Raw model response:");
        Console.WriteLine("-------------------");
        Console.WriteLine(rawResponse);

        Console.WriteLine();
        Console.WriteLine("Parsed analysis:");
        Console.WriteLine("----------------");
        Console.WriteLine(JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true }));

        return 0;
    }

    Console.WriteLine($"Unknown command: {args[0]}");
    Console.WriteLine();
    PrintUsage();
    return 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
    return 2;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Fatal error:");
    Console.WriteLine(ex.Message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("PhotoAIApp");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine(@"  PhotoAIApp.exe analyze ""D:\photoaitest\inputs5\2026 Caribbean Cruise1.jpg""");
    Console.WriteLine(@"  PhotoAIApp.exe scan ""D:\photoaitest\inputs5""");
    Console.WriteLine(@"  PhotoAIApp.exe scan ""D:\photoaitest\inputs5"" --recursive");
    Console.WriteLine(@"  PhotoAIApp.exe scan ""D:\photoaitest\inputs5"" --force --write-xmp");
    Console.WriteLine(@"  PhotoAIApp.exe scan ""P:\2025\Small Folder"" --safety-root ""P:\"" --dry-run --write-xmp");
    Console.WriteLine(@"  PhotoAIApp.exe scan ""P:\2025\Small Folder"" --safety-root ""P:\"" --force --write-xmp");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  analyze <image-path>       Analyze one image and print the model response.");
    Console.WriteLine("  scan <folder-path>         Analyze supported images in one folder.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --recursive                Include subfolders.");
    Console.WriteLine("  --force                    Reprocess images even if PhotoAI sidecar exists.");
    Console.WriteLine("  --write-xmp                Also write Immich-compatible .xmp sidecars.");
    Console.WriteLine("  --overwrite-json          Replace existing .photoai.json sidecars when reprocessing.");
    Console.WriteLine("  --overwrite-xmp           Replace existing .jpg.xmp sidecars.");
    Console.WriteLine("  --add-tags                Write tags/nouns into XMP keyword/tag metadata fields.");
    Console.WriteLine("  --dry-run                  Preview what would happen without calling Ollama or writing files.");
    Console.WriteLine("  --limit <number>           Process only the first N images.");
    Console.WriteLine("  --safety-root <folder>     Block processing unless selected folder is under this root.");
    Console.WriteLine();
    Console.WriteLine("Notes:");
    Console.WriteLine(@"  scan creates a per-run anomaly log in <scan-root>\.photoai\.");
}

static int? ReadIntOption(string[] args, string optionName)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int value))
        {
            return value;
        }
    }

    return null;
}

static string? ReadStringOption(string[] args, string optionName)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}
