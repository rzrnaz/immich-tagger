using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoAIApp.Core;

public sealed class PhotoAiScanner
{
    public async Task<PhotoAiScanSummary> ScanFolderAsync(
        PhotoAiScanOptions options,
        IProgress<PhotoAiScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.FolderPath))
        {
            throw new ArgumentException("Folder path is required.", nameof(options));
        }

        if (!Directory.Exists(options.FolderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {options.FolderPath}");
        }

        string rootPath = NormalizeDirectoryPath(options.FolderPath);
        string safetyRootPath = string.IsNullOrWhiteSpace(options.SafetyRootPath)
            ? rootPath
            : NormalizeDirectoryPath(options.SafetyRootPath);

        if (!Directory.Exists(safetyRootPath))
        {
            throw new DirectoryNotFoundException($"Safety root not found: {safetyRootPath}");
        }

        if (!IsPathUnderRoot(rootPath, safetyRootPath))
        {
            throw new InvalidOperationException($"Selected folder is outside the configured library/safety root. Selected: {rootPath}; Safety root: {safetyRootPath}");
        }

        string runLogPath = options.DryRun
            ? "(dry run - no anomaly log written)"
            : CreateRunLogPath(rootPath);
        DateTimeOffset startTime = DateTimeOffset.Now;
        var summary = new PhotoAiScanSummary
        {
            RootPath = rootPath,
            RunLogPath = runLogPath,
            StartTime = startTime,
            DryRun = options.DryRun
        };

        PhotoAiModelEndpoint[] modelEndpoints = BuildModelEndpointPlan(options);
        PhotoAiModelEndpoint primaryEndpoint = modelEndpoints[0];

        Report(
            progress,
            "RUN START",
            $"Scan root: {rootPath}",
            snapshot: BuildProgressSnapshot(summary, options, PhotoAiRunState.Scanning, "Scanning folder", totalFiles: null));
        Report(progress, "INFO", $"Safety root: {safetyRootPath}");
        Report(progress, "INFO", $"Subfolders: {options.Recursive}; Scan Existing: {options.Force}; Overwrite sidecars JSON+XMP: {options.OverwriteSidecars}; Add tags: {options.AddTags}; Dry run: {options.DryRun}; Max Image Size: {FormatMaxImageDimension(options.MaxImageDimensionPixels)}; Fallback Max Image Size: {FormatMaxImageDimension(options.FallbackMaxImageDimensionPixels)}; Model preference: {options.ModelPreference}; Primary: {primaryEndpoint.Model} @ {primaryEndpoint.OllamaBaseUrl}; Fallback: {(modelEndpoints.Length > 1 ? $"{modelEndpoints[1].Model} @ {modelEndpoints[1].OllamaBaseUrl}" : "none")}");

        if (options.DryRun)
        {
            Report(progress, "DRY RUN", "Preview mode is enabled. No Ollama calls will be made and no files will be written.");
        }
        else
        {
            Report(progress, "INFO", $"Anomaly log: {runLogPath}");
            await AppendRunLogAsync(runLogPath, "RUN START", new[]
            {
                $"scan_root={rootPath}",
                $"safety_root={safetyRootPath}",
                $"recursive={options.Recursive}",
                $"force={options.Force}",
                $"write_json={options.WriteJson}",
                $"write_xmp={options.WriteXmp}",
                $"overwrite_sidecars={options.OverwriteSidecars}",
                $"add_tags={options.AddTags}",
                $"dry_run={options.DryRun}",
                $"ollama={primaryEndpoint.OllamaBaseUrl}",
                $"model={primaryEndpoint.Model}",
                $"model_preference={options.ModelPreference}",
                $"fallback_ollama={(modelEndpoints.Length > 1 ? modelEndpoints[1].OllamaBaseUrl : "none")}",
                $"fallback_model={(modelEndpoints.Length > 1 ? modelEndpoints[1].Model : "none")}",
                $"max_image_dimension_pixels={options.MaxImageDimensionPixels}",
                $"max_image_dimension_label={FormatMaxImageDimension(options.MaxImageDimensionPixels)}",
                $"fallback_max_image_dimension_pixels={options.FallbackMaxImageDimensionPixels}",
                $"fallback_max_image_dimension_label={FormatMaxImageDimension(options.FallbackMaxImageDimensionPixels)}",
                $"limit={options.Limit?.ToString() ?? "none"}"
            }, cancellationToken);
        }

        List<string> imagePaths = GetSupportedImagePaths(rootPath, options.Recursive).ToList();

        summary.ImagesFound = imagePaths.Count;
        Report(
            progress,
            "IMAGES FOUND",
            $"Images found: {imagePaths.Count}",
            snapshot: BuildProgressSnapshot(
                summary,
                options,
                options.DryRun ? PhotoAiRunState.DryRunning : PhotoAiRunState.Running,
                options.DryRun ? "Dry run preview" : "Generating descriptions",
                imagePaths.Count));

        if (options.Limit is > 0)
        {
            Report(progress, "LIMIT", $"Processing up to {options.Limit.Value} non-skipped images due to limit.");
        }

        if (imagePaths.Count == 0)
        {
            summary.StopTime = DateTimeOffset.Now;
            if (!options.DryRun)
            {
                await AppendRunLogAsync(runLogPath, "NO SUPPORTED IMAGES", new[]
                {
                    $"scan_root={rootPath}",
                    $"start_time={FormatTimestamp(summary.StartTime)}",
                    $"stop_time={FormatTimestamp(summary.StopTime)}",
                    $"elapsed={FormatDuration(summary.ElapsedTime)}"
                }, cancellationToken);
            }
            return summary;
        }

        if (options.DryRun)
        {
            await PreviewScanAsync(imagePaths, rootPath, safetyRootPath, options, summary, progress, cancellationToken);
            summary.StopTime = DateTimeOffset.Now;
            Report(
                progress,
                "DRY RUN COMPLETE",
                "Preview complete. No files were written.",
                snapshot: BuildProgressSnapshot(summary, options, PhotoAiRunState.Completed, "Dry run complete", imagePaths.Count));
            foreach (string summaryLine in BuildDryRunSummaryLines(summary))
            {
                Report(progress, "SUMMARY", summaryLine);
            }
            return summary;
        }

        for (int index = 0; index < imagePaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(options, progress, cancellationToken);

            string fullImagePath = Path.GetFullPath(imagePaths[index]);
            int displayIndex = index + 1;

            if (!IsPathUnderRoot(fullImagePath, rootPath))
            {
                summary.Failed++;
                Report(progress, "BLOCKED", $"BLOCKED outside selected folder: {fullImagePath}", fullImagePath);
                await AppendRunLogAsync(runLogPath, "BLOCKED OUTSIDE SELECTED ROOT", new[]
                {
                    $"image={fullImagePath}",
                    $"scan_root={rootPath}"
                }, cancellationToken);
                continue;
            }

            if (!IsPathUnderRoot(fullImagePath, safetyRootPath))
            {
                summary.Failed++;
                Report(progress, "BLOCKED", $"BLOCKED outside safety root: {fullImagePath}", fullImagePath);
                await AppendRunLogAsync(runLogPath, "BLOCKED OUTSIDE SAFETY ROOT", new[]
                {
                    $"image={fullImagePath}",
                    $"safety_root={safetyRootPath}"
                }, cancellationToken);
                continue;
            }

            string photoAiSidecarPath = GetPhotoAiSidecarPath(fullImagePath);
            string xmpPath = GetImmichXmpSidecarPath(fullImagePath);
            bool jsonExists = File.Exists(photoAiSidecarPath);
            bool xmpExists = File.Exists(xmpPath);

            if (options.WriteJson && jsonExists && !options.Force)
            {
                summary.Skipped++;
                Report(
                    progress,
                    "SKIP",
                    $"SKIP {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: existing PhotoAI sidecar and Scan Existing is off: {Path.GetFileName(fullImagePath)}",
                    fullImagePath,
                    BuildProgressSnapshot(summary, options, PhotoAiRunState.Running, "Generating descriptions", imagePaths.Count));
                continue;
            }

            PhotoAiSidecarWritePlan writePlan = PlanSidecarWrites(options, jsonExists, xmpExists);
            if (writePlan.ShouldSkipWithoutAnalysis)
            {
                summary.Skipped++;
                if (options.WriteJson && jsonExists)
                {
                    summary.JsonWriteSkipped++;
                }
                if (options.WriteXmp && xmpExists)
                {
                    summary.XmpWriteSkipped++;
                }

                Report(
                    progress,
                    "SKIP",
                    $"SKIP {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: JSON/XMP sidecars already exist and sidecar overwrite is off, so no LLM call is needed: {fullImagePath}",
                    fullImagePath,
                    BuildProgressSnapshot(summary, options, PhotoAiRunState.Running, "Generating descriptions", imagePaths.Count));
                continue;
            }

            int attemptedNonSkippedImages = summary.Completed + summary.Failed;
            if (options.Limit is > 0 && attemptedNonSkippedImages >= options.Limit.Value)
            {
                Report(progress, "LIMIT", $"Limit reached after processing {attemptedNonSkippedImages} non-skipped images.");
                break;
            }

            Report(progress, "PROCESS", $"PROCESS {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: true)}: {fullImagePath}", fullImagePath);

            try
            {
                AnalysisResult analysisResult = await AnalyzeImageWithFallbackAsync(fullImagePath, modelEndpoints, options, summary, runLogPath, progress, cancellationToken);
                string rawResponse = analysisResult.RawResponse;
                PhotoAnalysis? analysis = TryParsePhotoAnalysis(rawResponse);
                analysis = EnrichAnalysisTags(analysis);

                var sidecar = new PhotoAiSidecar
                {
                    SourceFile = fullImagePath,
                    SourceFileName = Path.GetFileName(fullImagePath),
                    SourceRoot = rootPath,
                    RelativePath = Path.GetRelativePath(rootPath, fullImagePath),
                    OllamaBaseUrl = analysisResult.Endpoint.OllamaBaseUrl,
                    Model = analysisResult.Endpoint.Model,
                    ProcessedAtUtc = DateTimeOffset.UtcNow,
                    Analysis = analysis,
                    RawResponse = rawResponse,
                    ImageProcessing = analysisResult.ImageProcessing,
                    OllamaStats = analysisResult.OllamaStats,
                    ModelAttempts = analysisResult.ModelAttempts.ToArray()
                };

                var writtenSidecarDisplayNames = new List<string>();

                if (writePlan.CanWriteJson)
                {
                    string json = PhotoAiSidecarSerializer.Serialize(sidecar);

                    await File.WriteAllTextAsync(photoAiSidecarPath, json, cancellationToken);
                    writtenSidecarDisplayNames.Add(photoAiSidecarPath);
                }
                else if (options.WriteJson)
                {
                    summary.JsonWriteSkipped++;
                    Report(progress, "JSON SKIPPED", $"JSON SKIPPED: existing file and sidecar overwrite is off: {photoAiSidecarPath}", fullImagePath);
                }

                if (options.WriteXmp)
                {
                    if (analysis is null)
                    {
                        summary.ParseFailed++;
                        string parseMessage = options.WriteJson
                            ? $"XMP SKIPPED: model response could not be parsed. Raw response saved in {photoAiSidecarPath}"
                            : "XMP SKIPPED: model response could not be parsed. JSON sidecar writing is off, so raw response was not saved.";
                        Report(progress, "PARSE FAILED", parseMessage, fullImagePath);
                        await AppendRunLogAsync(runLogPath, "PARSE FAILED / XMP SKIPPED", new[]
                        {
                            $"image={fullImagePath}",
                            $"photoai_sidecar={photoAiSidecarPath}",
                            $"raw_response_length={rawResponse.Length}",
                            $"raw_response_excerpt={OneLineExcerpt(rawResponse, 1200)}"
                        }, cancellationToken);
                    }
                    else
                    {
                        if (writePlan.CanWriteXmp)
                        {
                            string xmp = BuildImmichXmp(analysis, options.AddTags);
                            await File.WriteAllTextAsync(xmpPath, xmp, cancellationToken);
                            summary.XmpWritten++;
                            writtenSidecarDisplayNames.Add(Path.GetFileName(xmpPath));
                        }
                        else
                        {
                            summary.XmpWriteSkipped++;
                            Report(progress, "XMP SKIPPED", $"XMP SKIPPED: existing file and sidecar overwrite is off: {xmpPath}", fullImagePath);
                        }
                    }
                }

                if (writtenSidecarDisplayNames.Count > 0)
                {
                    Report(progress, "WROTE", $"Wrote {string.Join(", ", writtenSidecarDisplayNames)}", fullImagePath);
                }

                summary.Completed++;
                Report(
                    progress,
                    "PROGRESS",
                    $"Completed {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: {Path.GetFileName(fullImagePath)}",
                    fullImagePath,
                    BuildProgressSnapshot(summary, options, PhotoAiRunState.Running, "Generating descriptions", imagePaths.Count));
            }
            catch (OperationCanceledException)
            {
                Report(
                    progress,
                    "CANCELLED",
                    "Scan cancelled by user.",
                    snapshot: BuildProgressSnapshot(summary, options, PhotoAiRunState.Cancelled, "Cancelled", imagePaths.Count));
                await AppendRunLogAsync(runLogPath, "CANCELLED", new[] { $"image={fullImagePath}" }, CancellationToken.None);
                await UnloadModelEndpointsAsync(modelEndpoints, progress, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                summary.Failed++;
                Report(
                    progress,
                    "FAILED",
                    $"FAILED {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: {fullImagePath}: {ex.Message}",
                    fullImagePath,
                    BuildProgressSnapshot(summary, options, PhotoAiRunState.Running, "Generating descriptions", imagePaths.Count));
                await AppendRunLogAsync(runLogPath, "FAILED", new[]
                {
                    $"image={fullImagePath}",
                    $"exception_type={ex.GetType().FullName}",
                    $"message={ex.Message}"
                }, cancellationToken);
            }
        }

        summary.StopTime = DateTimeOffset.Now;
        Report(
            progress,
            "RUN COMPLETE",
            "Scan complete.",
            snapshot: BuildProgressSnapshot(summary, options, PhotoAiRunState.Completed, "Run complete", imagePaths.Count));
        foreach (string summaryLine in BuildRunSummaryLines(summary))
        {
            Report(progress, "SUMMARY", summaryLine);
        }

        await AppendRunLogAsync(runLogPath, "RUN COMPLETE", new[]
        {
            $"images_found={summary.ImagesFound}",
            $"start_time={FormatTimestamp(summary.StartTime)}",
            $"stop_time={FormatTimestamp(summary.StopTime)}",
            $"elapsed={FormatDuration(summary.ElapsedTime)}",
            $"average_time_per_processed_photo={FormatAverageSecondsPerPhoto(summary.AverageTimePerProcessedPhoto)}",
            $"completed={summary.Completed}",
            $"skipped={summary.Skipped}",
            $"failed={summary.Failed}",
            $"parse_xmp_skipped={summary.ParseFailed}",
            $"xmp_written={summary.XmpWritten}",
            $"json_write_skipped={summary.JsonWriteSkipped}",
            $"xmp_write_skipped={summary.XmpWriteSkipped}",
            $"model_failures={summary.ModelFailures}",
            $"primary_retry_attempts={summary.PrimaryRetryAttempts}",
            $"primary_retry_successes={summary.PrimaryRetrySucceeded}",
            $"primary_retry_failures={summary.PrimaryRetryFailed}",
            $"fallback_attempts={summary.FallbackAttempts}",
            $"fallback_successes={summary.FallbackSucceeded}"
        }, cancellationToken);

        if (options.UnloadModelsAtEnd)
        {
            await UnloadModelEndpointsAsync(modelEndpoints, progress, CancellationToken.None);
        }

        return summary;
    }

    private static async Task PreviewScanAsync(
        List<string> imagePaths,
        string rootPath,
        string safetyRootPath,
        PhotoAiScanOptions options,
        PhotoAiScanSummary summary,
        IProgress<PhotoAiScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < imagePaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(options, progress, cancellationToken);

            string fullImagePath = Path.GetFullPath(imagePaths[index]);
            int displayIndex = index + 1;

            if (!IsPathUnderRoot(fullImagePath, rootPath))
            {
                summary.Failed++;
                Report(progress, "WOULD BLOCK", $"WOULD BLOCK outside selected folder: {fullImagePath}", fullImagePath);
                continue;
            }

            if (!IsPathUnderRoot(fullImagePath, safetyRootPath))
            {
                summary.Failed++;
                Report(progress, "WOULD BLOCK", $"WOULD BLOCK outside safety root: {fullImagePath}", fullImagePath);
                continue;
            }

            string photoAiSidecarPath = GetPhotoAiSidecarPath(fullImagePath);
            string xmpPath = GetImmichXmpSidecarPath(fullImagePath);
            bool hasPhotoAiSidecar = File.Exists(photoAiSidecarPath);
            bool hasXmpSidecar = File.Exists(xmpPath);

            if (hasPhotoAiSidecar)
            {
                summary.ExistingJsonSidecars++;
            }

            if (hasXmpSidecar)
            {
                summary.ExistingXmpSidecars++;
            }

            if (options.WriteJson && hasPhotoAiSidecar && !options.Force)
            {
                summary.Skipped++;
                Report(
                    progress,
                    "WOULD SKIP",
                    $"WOULD SKIP {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: existing PhotoAI sidecar and Force is off: {fullImagePath}",
                    fullImagePath,
                    BuildProgressSnapshot(summary, options, PhotoAiRunState.DryRunning, "Dry run preview", imagePaths.Count));
                continue;
            }

            PhotoAiSidecarWritePlan writePlan = PlanSidecarWrites(options, hasPhotoAiSidecar, hasXmpSidecar);
            if (writePlan.ShouldSkipWithoutAnalysis)
            {
                summary.Skipped++;
                if (options.WriteJson && hasPhotoAiSidecar)
                {
                    summary.WouldSkipJsonSidecar++;
                }
                if (options.WriteXmp && hasXmpSidecar)
                {
                    summary.WouldSkipXmpSidecar++;
                }

                Report(
                    progress,
                    "WOULD SKIP",
                    $"WOULD SKIP {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: JSON/XMP sidecars already exist and sidecar overwrite is off, so no LLM call would be made: {fullImagePath}",
                    fullImagePath,
                    BuildProgressSnapshot(summary, options, PhotoAiRunState.DryRunning, "Dry run preview", imagePaths.Count));
                continue;
            }

            if (options.Limit is > 0 && summary.WouldProcess >= options.Limit.Value)
            {
                Report(progress, "LIMIT", $"Limit reached after planning {summary.WouldProcess} non-skipped images.");
                break;
            }

            summary.WouldProcess++;
            bool wouldWriteJson = writePlan.CanWriteJson;
            bool wouldWriteXmp = writePlan.CanWriteXmp;

            if (wouldWriteJson)
            {
                summary.WouldWriteJsonSidecar++;
            }
            else if (options.WriteJson)
            {
                summary.WouldSkipJsonSidecar++;
            }

            if (wouldWriteXmp)
            {
                summary.WouldWriteXmpSidecar++;
            }
            else if (options.WriteXmp)
            {
                summary.WouldSkipXmpSidecar++;
            }

            Report(
                progress,
                "WOULD PROCESS",
                $"WOULD PROCESS {FormatProgressLabel(options, displayIndex, imagePaths.Count, summary, includeCurrentFile: false)}: {fullImagePath}",
                fullImagePath,
                BuildProgressSnapshot(summary, options, PhotoAiRunState.DryRunning, "Dry run preview", imagePaths.Count));

            if (wouldWriteJson)
            {
                Report(progress, "WOULD WRITE", $"  WOULD WRITE {photoAiSidecarPath}", fullImagePath);
            }
            else if (options.WriteJson)
            {
                Report(progress, "WOULD SKIP", $"  WOULD SKIP JSON existing file, sidecar overwrite off: {photoAiSidecarPath}", fullImagePath);
            }

            if (wouldWriteXmp)
            {
                Report(progress, "WOULD WRITE", $"  WOULD WRITE {xmpPath}", fullImagePath);
            }
            else if (options.WriteXmp)
            {
                Report(progress, "WOULD SKIP", $"  WOULD SKIP XMP existing file, sidecar overwrite off: {xmpPath}", fullImagePath);
            }
        }
    }

    public static PhotoAiModelEndpoint[] BuildModelEndpointPlan(PhotoAiScanOptions options)
    {
        string unraidUrl = CleanRequiredValue(options.FallbackOllamaBaseUrl, PhotoAiDefaults.UnraidOllamaBaseUrl);
        string unraidModel = CleanRequiredValue(options.FallbackModel, PhotoAiDefaults.UnraidModel);

        if (options.ModelPreference == PhotoAiModelPreference.UnraidMiniCpmOnly)
        {
            return [new PhotoAiModelEndpoint(unraidUrl, unraidModel, IsFallback: false, options.FallbackMaxImageDimensionPixels)];
        }

        string primaryUrl = CleanRequiredValue(options.OllamaBaseUrl, PhotoAiDefaults.QwenPcOllamaBaseUrl);
        string primaryModel = CleanRequiredValue(options.Model, PhotoAiDefaults.QwenPcModel);

        if (options.ModelPreference == PhotoAiModelPreference.PrimaryOnly)
        {
            return [new PhotoAiModelEndpoint(primaryUrl, primaryModel, IsFallback: false, options.MaxImageDimensionPixels)];
        }

        if (string.Equals(primaryUrl, unraidUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(primaryModel, unraidModel, StringComparison.OrdinalIgnoreCase))
        {
            return [new PhotoAiModelEndpoint(primaryUrl, primaryModel, IsFallback: false, options.MaxImageDimensionPixels)];
        }

        return
        [
            new PhotoAiModelEndpoint(primaryUrl, primaryModel, IsFallback: false, options.MaxImageDimensionPixels),
            new PhotoAiModelEndpoint(unraidUrl, unraidModel, IsFallback: true, options.FallbackMaxImageDimensionPixels)
        ];
    }

    public static PhotoAiSidecarWritePlan PlanSidecarWrites(
        PhotoAiScanOptions options,
        bool jsonExists,
        bool xmpExists)
    {
        bool overwriteSidecars = options.OverwriteSidecars;
        bool canWriteJson = options.WriteJson && (!jsonExists || overwriteSidecars);
        bool canWriteXmp = options.WriteXmp && (!xmpExists || overwriteSidecars);
        return new PhotoAiSidecarWritePlan(overwriteSidecars, canWriteJson, canWriteXmp);
    }

    private static string CleanRequiredValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed record AnalysisResult(
        string RawResponse,
        PhotoAiModelEndpoint Endpoint,
        PhotoAiImageProcessingMetadata? ImageProcessing,
        PhotoAiOllamaStats? OllamaStats,
        IReadOnlyList<PhotoAiModelAttemptLog> ModelAttempts);

    private sealed record OllamaImageAnalysisResult(
        string RawResponse,
        PhotoAiImageProcessingMetadata ImageProcessing,
        PhotoAiOllamaStats OllamaStats);

    private sealed record ImagePayload(byte[] Bytes, ImageResizeResult Resize, long OriginalByteCount)
    {
        public long ByteCount => Bytes.LongLength;
    }

    public sealed record ImageResizeResult(
        int OriginalWidth,
        int OriginalHeight,
        int Width,
        int Height,
        bool WasResized,
        TimeSpan Elapsed);

    public static ImageResizeResult CalculateResizeDimensions(int originalWidth, int originalHeight, int maxImageDimensionPixels)
    {
        if (originalWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalWidth), "Original width must be positive.");
        }

        if (originalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalHeight), "Original height must be positive.");
        }

        if (maxImageDimensionPixels <= 0)
        {
            return new ImageResizeResult(originalWidth, originalHeight, originalWidth, originalHeight, false, TimeSpan.Zero);
        }

        int longestEdge = Math.Max(originalWidth, originalHeight);
        if (longestEdge <= maxImageDimensionPixels)
        {
            return new ImageResizeResult(originalWidth, originalHeight, originalWidth, originalHeight, false, TimeSpan.Zero);
        }

        double scale = (double)maxImageDimensionPixels / longestEdge;
        int resizedWidth = Math.Max(1, (int)Math.Round(originalWidth * scale, MidpointRounding.AwayFromZero));
        int resizedHeight = Math.Max(1, (int)Math.Round(originalHeight * scale, MidpointRounding.AwayFromZero));

        return new ImageResizeResult(originalWidth, originalHeight, resizedWidth, resizedHeight, true, TimeSpan.Zero);
    }

    private static async Task<ImagePayload> PrepareImageForOllamaAsync(string imagePath, int maxImageDimensionPixels, CancellationToken cancellationToken)
    {
        long originalByteCount = new FileInfo(imagePath).Length;
        var stopwatch = Stopwatch.StartNew();

        using Image image = await Image.LoadAsync(imagePath, cancellationToken);
        ImageResizeResult plannedResize = CalculateResizeDimensions(image.Width, image.Height, maxImageDimensionPixels);

        if (!plannedResize.WasResized)
        {
            byte[] originalBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            stopwatch.Stop();
            return new ImagePayload(
                originalBytes,
                plannedResize with { Elapsed = stopwatch.Elapsed },
                originalByteCount);
        }

        image.Mutate(operation => operation.Resize(plannedResize.Width, plannedResize.Height));
        await using var resizedStream = new MemoryStream();
        await image.SaveAsJpegAsync(resizedStream, new JpegEncoder { Quality = 90 }, cancellationToken);
        stopwatch.Stop();

        return new ImagePayload(
            resizedStream.ToArray(),
            plannedResize with { Elapsed = stopwatch.Elapsed },
            originalByteCount);
    }

    private static async Task SetModelKeepAliveAsync(
        PhotoAiModelEndpoint endpoint,
        string keepAlive,
        string eventName,
        IProgress<PhotoAiScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, eventName, $"Setting {endpoint.Model} keep_alive={keepAlive} at {endpoint.OllamaBaseUrl}.");

        using HttpClient http = new()
        {
            BaseAddress = new Uri(endpoint.OllamaBaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        var request = new OllamaGenerateRequest
        {
            Model = endpoint.Model,
            Prompt = "",
            Images = [],
            Stream = false,
            KeepAlive = keepAlive
        };

        HttpResponseMessage response = await http.PostAsJsonAsync("/api/generate", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama keep_alive request failed for {endpoint.Model} at {endpoint.OllamaBaseUrl}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
        }
    }

    private static async Task UnloadModelEndpointsAsync(
        IReadOnlyList<PhotoAiModelEndpoint> endpoints,
        IProgress<PhotoAiScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (PhotoAiModelEndpoint endpoint in endpoints.DistinctBy(endpoint => (endpoint.OllamaBaseUrl, endpoint.Model)))
        {
            try
            {
                await SetModelKeepAliveAsync(endpoint, PhotoAiDefaults.OllamaUnloadKeepAlive, "MODEL UNLOAD", progress, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Report(progress, "MODEL UNLOAD FAILED", $"Could not unload {endpoint.Model} at {endpoint.OllamaBaseUrl}: {ex.Message}");
            }
        }
    }

    private async Task<AnalysisResult> AnalyzeImageWithFallbackAsync(
        string imagePath,
        IReadOnlyList<PhotoAiModelEndpoint> endpoints,
        PhotoAiScanOptions options,
        PhotoAiScanSummary summary,
        string runLogPath,
        IProgress<PhotoAiScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? primaryException = null;
        var attemptLogs = new List<PhotoAiModelAttemptLog>();
        int attemptNumber = 0;

        for (int endpointIndex = 0; endpointIndex < endpoints.Count; endpointIndex++)
        {
            PhotoAiModelEndpoint endpoint = endpoints[endpointIndex];
            int maxEndpointAttempts = endpoint.IsFallback ? 1 : 2;

            if (endpoint.IsFallback)
            {
                summary.FallbackAttempts++;
                Report(progress, "FALLBACK", $"Primary model failed after two attempts; trying fallback {endpoint.Model} at {endpoint.OllamaBaseUrl}. This is recoverable; scan will continue if fallback succeeds.", imagePath);
            }

            for (int endpointAttempt = 1; endpointAttempt <= maxEndpointAttempts; endpointAttempt++)
            {
                attemptNumber++;
                TimeSpan timeout = TimeSpan.FromMinutes(PhotoAiDefaults.OllamaAnalysisTimeoutMinutes);
                DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

                try
                {
                    if (!endpoint.IsFallback && endpointAttempt == 2)
                    {
                        summary.PrimaryRetryAttempts++;
                        Report(progress, "PRIMARY RETRY", $"Retrying primary model {endpoint.Model} at {endpoint.OllamaBaseUrl} after a transient-looking Ollama failure.", imagePath);
                    }

                    OllamaImageAnalysisResult attemptResult = await AnalyzeImageWithDiagnosticsAsync(
                        imagePath,
                        endpoint.OllamaBaseUrl,
                        endpoint.Model,
                        progress,
                        cancellationToken,
                        timeout,
                        endpoint.MaxImageDimensionPixels,
                        runLogPath,
                        attemptNumber,
                        endpoint.IsFallback,
                        startedAtUtc);
                    attemptLogs.Add(new PhotoAiModelAttemptLog
                    {
                        AttemptNumber = attemptNumber,
                        OllamaBaseUrl = endpoint.OllamaBaseUrl,
                        Model = endpoint.Model,
                        IsFallback = endpoint.IsFallback,
                        StartedAtUtc = startedAtUtc,
                        CompletedAtUtc = DateTimeOffset.UtcNow,
                        Succeeded = true,
                        TimeoutSeconds = timeout.TotalSeconds,
                        ImageProcessing = attemptResult.ImageProcessing,
                        OllamaStats = attemptResult.OllamaStats
                    });
                    if (endpoint.IsFallback)
                    {
                        summary.FallbackSucceeded++;
                    }
                    else if (endpointAttempt == 2)
                    {
                        summary.PrimaryRetrySucceeded++;
                    }

                    return new AnalysisResult(
                        attemptResult.RawResponse,
                        endpoint,
                        attemptResult.ImageProcessing,
                        attemptResult.OllamaStats,
                        attemptLogs);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    summary.ModelFailures++;
                    if (!endpoint.IsFallback && endpointAttempt == 2)
                    {
                        summary.PrimaryRetryFailed++;
                    }

                    if (!endpoint.IsFallback)
                    {
                        primaryException ??= ex;
                    }

                    var failedAttempt = new PhotoAiModelAttemptLog
                    {
                        AttemptNumber = attemptNumber,
                        OllamaBaseUrl = endpoint.OllamaBaseUrl,
                        Model = endpoint.Model,
                        IsFallback = endpoint.IsFallback,
                        StartedAtUtc = startedAtUtc,
                        CompletedAtUtc = DateTimeOffset.UtcNow,
                        Succeeded = false,
                        TimeoutSeconds = timeout.TotalSeconds,
                        ErrorType = ex.GetType().FullName,
                        ErrorMessage = ex.Message
                    };
                    attemptLogs.Add(failedAttempt);
                    await AppendRunLogAsync(runLogPath, "MODEL ATTEMPT FAILED", new[]
                    {
                        $"image={imagePath}",
                        $"attempt_number={attemptNumber}",
                        $"endpoint_attempt={endpointAttempt}",
                        $"ollama={endpoint.OllamaBaseUrl}",
                        $"model={endpoint.Model}",
                        $"is_fallback={endpoint.IsFallback}",
                        $"timeout_seconds={timeout.TotalSeconds}",
                        $"max_image_dimension_pixels={endpoint.MaxImageDimensionPixels}",
                        $"exception_type={ex.GetType().FullName}",
                        $"message={ex.Message}",
                        $"full_exception={OneLineExcerpt(ex.ToString(), 4000)}"
                    }, cancellationToken);

                    bool shouldRetryPrimary =
                        !endpoint.IsFallback &&
                        endpointAttempt < maxEndpointAttempts &&
                        IsTransientOllamaFailure(ex);

                    if (shouldRetryPrimary)
                    {
                        await AppendRunLogAsync(runLogPath, "PRIMARY RETRY SCHEDULED", new[]
                        {
                            $"image={imagePath}",
                            $"failed_attempt_number={attemptNumber}",
                            $"next_attempt_number={attemptNumber + 1}",
                            $"retry_delay_ms={PhotoAiDefaults.PrimaryTransientRetryDelayMilliseconds}",
                            $"ollama={endpoint.OllamaBaseUrl}",
                            $"model={endpoint.Model}",
                            $"reason={OneLineExcerpt(ex.Message, 1000)}"
                        }, cancellationToken);
                        Report(progress, "PRIMARY RETRY", $"Primary model failed with a transient-looking Ollama error; retrying once before fallback: {ex.Message}", imagePath);
                        await Task.Delay(PhotoAiDefaults.PrimaryTransientRetryDelayMilliseconds, cancellationToken);
                        continue;
                    }

                    if (!endpoint.IsFallback && endpointAttempt < maxEndpointAttempts)
                    {
                        await AppendRunLogAsync(runLogPath, "PRIMARY RETRY SKIPPED", new[]
                        {
                            $"image={imagePath}",
                            $"failed_attempt_number={attemptNumber}",
                            $"ollama={endpoint.OllamaBaseUrl}",
                            $"model={endpoint.Model}",
                            $"reason=non_transient_error",
                            $"message={OneLineExcerpt(ex.Message, 1000)}"
                        }, cancellationToken);
                    }

                    bool isLastEndpoint = endpointIndex == endpoints.Count - 1;
                    bool isLastEndpointAttempt = endpointAttempt == maxEndpointAttempts || (!endpoint.IsFallback && !shouldRetryPrimary);
                    if (isLastEndpoint && isLastEndpointAttempt)
                    {
                        if (primaryException is null || primaryException == ex)
                        {
                            throw;
                        }

                        throw new InvalidOperationException($"Primary and fallback model calls failed. Primary: {primaryException.Message}; Fallback: {ex.Message}", ex);
                    }

                    Report(progress, "MODEL FAILED", $"Model call failed for {endpoint.Model} at {endpoint.OllamaBaseUrl}: {ex.Message}", imagePath);
                    break;
                }
            }
        }

        throw new InvalidOperationException("No model endpoints were configured.");
    }

    private static bool IsTransientOllamaFailure(Exception ex)
    {
        string message = ex.ToString();
        return message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Failed to create new sequence", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("failed to process inputs", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatOllamaKeepAliveForLog(string? keepAlive)
    {
        return string.IsNullOrWhiteSpace(keepAlive)
            ? "omitted (Ollama default)"
            : keepAlive;
    }

    private static async Task WaitIfPausedAsync(PhotoAiScanOptions options, IProgress<PhotoAiScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (options.PauseController is null || !options.PauseController.IsPaused)
        {
            return;
        }

        Report(
            progress,
            "PAUSED",
            "Scan paused. Click Resume to continue.",
            snapshot: PhotoAiRunProgressSnapshot.Create(
                PhotoAiRunState.Paused,
                "Paused",
                DateTimeOffset.Now,
                DateTimeOffset.Now,
                totalFiles: null,
                completedFiles: 0,
                skippedFiles: 0,
                failedFiles: 0));
        try
        {
            await options.PauseController.WaitIfPausedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Report(
                progress,
                "CANCELLED",
                "Scan cancelled by user.",
                snapshot: PhotoAiRunProgressSnapshot.Create(
                    PhotoAiRunState.Cancelled,
                    "Cancelled",
                    DateTimeOffset.Now,
                    DateTimeOffset.Now,
                    totalFiles: null,
                    completedFiles: 0,
                    skippedFiles: 0,
                    failedFiles: 0));
            throw;
        }
        Report(
            progress,
            "RESUMED",
            "Scan resumed.",
            snapshot: PhotoAiRunProgressSnapshot.Create(
                PhotoAiRunState.Running,
                "Generating descriptions",
                DateTimeOffset.Now,
                DateTimeOffset.Now,
                totalFiles: null,
                completedFiles: 0,
                skippedFiles: 0,
                failedFiles: 0));
    }

    public async Task<string[]> GetAvailableOllamaModelsAsync(string ollamaBaseUrl, CancellationToken cancellationToken = default)
    {
        using HttpClient http = new()
        {
            BaseAddress = new Uri(ollamaBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };

        OllamaTagsResponse? tagsResponse;
        try
        {
            tagsResponse = await http.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Failed to query Ollama models at {ollamaBaseUrl}. Is Ollama running?", ex);
        }

        return tagsResponse?.Models?
            .Select(model => model.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    public async Task<string> AnalyzeImageAsync(
        string imagePath,
        string ollamaBaseUrl,
        string model,
        IProgress<PhotoAiScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        int maxImageDimensionPixels = PhotoAiDefaults.QwenMaxImageDimensionPixels)
    {
        OllamaImageAnalysisResult result = await AnalyzeImageWithDiagnosticsAsync(
            imagePath,
            ollamaBaseUrl,
            model,
            progress,
            cancellationToken,
            timeout,
            maxImageDimensionPixels,
            logPath: null,
            attemptNumber: 1,
            isFallback: false,
            startedAtUtc: DateTimeOffset.UtcNow);
        return result.RawResponse;
    }

    private async Task<OllamaImageAnalysisResult> AnalyzeImageWithDiagnosticsAsync(
        string imagePath,
        string ollamaBaseUrl,
        string model,
        IProgress<PhotoAiScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        int maxImageDimensionPixels = PhotoAiDefaults.QwenMaxImageDimensionPixels,
        string? logPath = null,
        int attemptNumber = 1,
        bool isFallback = false,
        DateTimeOffset? startedAtUtc = null)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image file not found: {imagePath}", imagePath);
        }

        DateTimeOffset requestStartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            await AppendRunLogAsync(logPath, "MODEL ATTEMPT START", new[]
            {
                $"image={imagePath}",
                $"attempt_number={attemptNumber}",
                $"ollama={ollamaBaseUrl}",
                $"model={model}",
                $"is_fallback={isFallback}",
                $"timeout_seconds={(timeout ?? TimeSpan.FromMinutes(PhotoAiDefaults.OllamaAnalysisTimeoutMinutes)).TotalSeconds}",
                $"max_image_dimension_pixels={maxImageDimensionPixels}",
                $"keep_alive={FormatOllamaKeepAliveForLog(PhotoAiDefaults.OllamaKeepAlive)}",
                "num_ctx=default_from_ollama_or_modelfile",
                $"started_at_utc={requestStartedAtUtc:O}"
            }, cancellationToken);
        }

        ImagePayload imagePayload = await PrepareImageForOllamaAsync(imagePath, maxImageDimensionPixels, cancellationToken);

        string imageBase64 = Convert.ToBase64String(imagePayload.Bytes);
        var imageProcessing = new PhotoAiImageProcessingMetadata
        {
            OriginalWidth = imagePayload.Resize.OriginalWidth,
            OriginalHeight = imagePayload.Resize.OriginalHeight,
            SentWidth = imagePayload.Resize.Width,
            SentHeight = imagePayload.Resize.Height,
            WasResized = imagePayload.Resize.WasResized,
            MaxImageDimensionPixels = maxImageDimensionPixels,
            OriginalBytes = imagePayload.OriginalByteCount,
            SentBytes = imagePayload.ByteCount,
            Base64Chars = imageBase64.Length,
            ResizeElapsedMilliseconds = imagePayload.Resize.Elapsed.TotalMilliseconds
        };

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            await AppendRunLogAsync(logPath, "IMAGE PAYLOAD", new[]
            {
                $"image={imagePath}",
                $"attempt_number={attemptNumber}",
                $"model={model}",
                $"original_dimensions={imageProcessing.OriginalWidth}x{imageProcessing.OriginalHeight}",
                $"sent_dimensions={imageProcessing.SentWidth}x{imageProcessing.SentHeight}",
                $"was_resized={imageProcessing.WasResized}",
                $"original_bytes={imageProcessing.OriginalBytes}",
                $"sent_bytes={imageProcessing.SentBytes}",
                $"base64_chars={imageProcessing.Base64Chars}",
                $"resize_elapsed_ms={imageProcessing.ResizeElapsedMilliseconds:F1}"
            }, cancellationToken);
        }

        string prompt = """
You are helping catalog a personal photo library.

Analyze this image and return practical photo-library metadata for search and review.

Return one valid JSON object only. Do not include markdown, code fences, comments, or explanatory text.
Use this exact structure:

{
  "description": "one useful sentence describing the visible photo content",
  "short_caption": "short caption",
  "tags": ["tag1", "tag2", "tag3"],
  "people_count": 0,
  "setting": "indoor/outdoor/unknown",
  "quality_flags": ["none"],
  "confidence": 0.0
}

Description rules:
- Describe only what is visibly supported by the image.
- Use conservative wording; prefer "a person" or "people" over identity, relationship, or role guesses.
- Do not invent names of people.
- Do not identify relationships such as spouse, child, parent, friend, or family unless visually explicit.
- Do not guess exact location unless obvious from visible signs or landmarks.
- Do not infer mechanical state, intent, or cause. Avoid phrases like canopy is up/down, parked, broken, waiting, relaxing, posing, or celebrating unless it is visually unambiguous.
- Avoid exact counts unless clear; if partly obscured, use approximate wording such as "several".
- Keep the description factual, concise, and natural. One sentence is usually enough.

Tag rules:
- Prefer 3 to 7 tags.
- Use concrete, visible subjects and settings that help search a photo library.
- Good tags are objects, places, scene types, and clearly visible attributes such as boat, water, car, kitchen, beach, dog, outdoor, indoor, people.
- Avoid mood/activity tags unless the activity is visually explicit; for example avoid relaxation, leisure, happiness, vacation, event, lifestyle, travel, and fun unless strongly supported.
- Do not include a tag just because it is true; include it only if it is useful for finding this image later.
- Avoid generic tags such as image, photo, picture, object, item, scene, environment, view, unknown, none.

Other rules:
- people_count should count visibly present people only; use 0 if unclear.
- setting must be indoor, outdoor, or unknown.
- quality_flags may include: blurry, dark, overexposed, screenshot, document, duplicate-looking, none.
- If unsure, say unknown or omit the uncertain detail.
- Return valid JSON only.
""";

        var request = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            Images = [imageBase64],
            Stream = false,
            Format = "json",
            KeepAlive = PhotoAiDefaults.OllamaKeepAlive
        };
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            await AppendRunLogAsync(logPath, "OLLAMA REQUEST", new[]
            {
                $"image={imagePath}",
                $"attempt_number={attemptNumber}",
                $"ollama={ollamaBaseUrl}",
                $"model={model}",
                $"prompt_chars={prompt.Length}",
                $"image_base64_chars={imageBase64.Length}",
                $"format=json",
                $"keep_alive={FormatOllamaKeepAliveForLog(PhotoAiDefaults.OllamaKeepAlive)}",
                "options=none"
            }, cancellationToken);
        }

        using HttpClient http = new()
        {
            BaseAddress = new Uri(ollamaBaseUrl),
            Timeout = timeout ?? TimeSpan.FromMinutes(PhotoAiDefaults.OllamaAnalysisTimeoutMinutes)
        };

        HttpResponseMessage response;
        var httpStopwatch = Stopwatch.StartNew();
        try
        {
            response = await http.PostAsJsonAsync("/api/generate", request, cancellationToken);
            httpStopwatch.Stop();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            httpStopwatch.Stop();
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                await AppendRunLogAsync(logPath, "OLLAMA CALL EXCEPTION", new[]
                {
                    $"image={imagePath}",
                    $"attempt_number={attemptNumber}",
                    $"ollama={ollamaBaseUrl}",
                    $"model={model}",
                    $"http_elapsed_ms={httpStopwatch.Elapsed.TotalMilliseconds:F1}",
                    $"exception_type={ex.GetType().FullName}",
                    $"message={ex.Message}",
                    $"full_exception={OneLineExcerpt(ex.ToString(), 4000)}"
                }, cancellationToken);
            }
            throw new InvalidOperationException($"Failed to call Ollama at {ollamaBaseUrl}. Is Ollama running?", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                await AppendRunLogAsync(logPath, "OLLAMA HTTP FAILED", new[]
                {
                    $"image={imagePath}",
                    $"attempt_number={attemptNumber}",
                    $"ollama={ollamaBaseUrl}",
                    $"model={model}",
                    $"status_code={(int)response.StatusCode}",
                    $"reason={response.ReasonPhrase}",
                    $"http_elapsed_ms={httpStopwatch.Elapsed.TotalMilliseconds:F1}",
                    $"error_body={OneLineExcerpt(errorBody, 4000)}"
                }, cancellationToken);
            }
            throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
        }

        OllamaGenerateResponse? ollamaResponse;
        try
        {
            ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                await AppendRunLogAsync(logPath, "OLLAMA RESPONSE PARSE FAILED", new[]
                {
                    $"image={imagePath}",
                    $"attempt_number={attemptNumber}",
                    $"ollama={ollamaBaseUrl}",
                    $"model={model}",
                    $"http_elapsed_ms={httpStopwatch.Elapsed.TotalMilliseconds:F1}",
                    $"raw_body_length={rawBody.Length}",
                    $"raw_body_excerpt={OneLineExcerpt(rawBody, 4000)}",
                    $"exception_type={ex.GetType().FullName}",
                    $"message={ex.Message}"
                }, cancellationToken);
            }
            throw new InvalidOperationException($"Failed to parse Ollama response. Raw response: {rawBody}", ex);
        }

        if (ollamaResponse is null)
        {
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                await AppendRunLogAsync(logPath, "OLLAMA EMPTY RESPONSE", new[]
                {
                    $"image={imagePath}",
                    $"attempt_number={attemptNumber}",
                    $"ollama={ollamaBaseUrl}",
                    $"model={model}",
                    $"http_elapsed_ms={httpStopwatch.Elapsed.TotalMilliseconds:F1}"
                }, cancellationToken);
            }
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        var ollamaStats = new PhotoAiOllamaStats
        {
            ModelReturned = ollamaResponse.Model,
            CreatedAt = ollamaResponse.CreatedAt,
            Done = ollamaResponse.Done,
            DoneReason = ollamaResponse.DoneReason,
            HttpElapsedMilliseconds = httpStopwatch.Elapsed.TotalMilliseconds,
            TotalDurationNanoseconds = ollamaResponse.TotalDurationNanoseconds,
            LoadDurationNanoseconds = ollamaResponse.LoadDurationNanoseconds,
            PromptEvalCount = ollamaResponse.PromptEvalCount,
            PromptEvalDurationNanoseconds = ollamaResponse.PromptEvalDurationNanoseconds,
            EvalCount = ollamaResponse.EvalCount,
            EvalDurationNanoseconds = ollamaResponse.EvalDurationNanoseconds,
            ResponseChars = ollamaResponse.Response.Length
        };

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            await AppendRunLogAsync(logPath, "OLLAMA RESPONSE", new[]
            {
                $"image={imagePath}",
                $"attempt_number={attemptNumber}",
                $"ollama={ollamaBaseUrl}",
                $"requested_model={model}",
                $"returned_model={ollamaStats.ModelReturned ?? "unknown"}",
                $"done={ollamaStats.Done}",
                $"done_reason={ollamaStats.DoneReason ?? "none"}",
                $"http_elapsed_ms={ollamaStats.HttpElapsedMilliseconds:F1}",
                $"total_duration_ns={ollamaStats.TotalDurationNanoseconds?.ToString() ?? "null"}",
                $"load_duration_ns={ollamaStats.LoadDurationNanoseconds?.ToString() ?? "null"}",
                $"prompt_eval_count={ollamaStats.PromptEvalCount?.ToString() ?? "null"}",
                $"prompt_eval_duration_ns={ollamaStats.PromptEvalDurationNanoseconds?.ToString() ?? "null"}",
                $"eval_count={ollamaStats.EvalCount?.ToString() ?? "null"}",
                $"eval_duration_ns={ollamaStats.EvalDurationNanoseconds?.ToString() ?? "null"}",
                $"response_chars={ollamaStats.ResponseChars}",
                $"response_excerpt={OneLineExcerpt(ollamaResponse.Response, 1200)}"
            }, cancellationToken);
        }

        return new OllamaImageAnalysisResult(ollamaResponse.Response, imageProcessing, ollamaStats);
    }

    public static PhotoAnalysis? TryParsePhotoAnalysis(string rawResponse)
    {
        string cleaned = ExtractJsonObject(rawResponse);

        try
        {
            return JsonSerializer.Deserialize<PhotoAnalysis>(
                cleaned,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }
            );
        }
        catch
        {
            return TryParsePhotoAnalysisLenient(cleaned);
        }
    }

    private static PhotoAnalysis? TryParsePhotoAnalysisLenient(string text)
    {
        string description = ExtractJsonStringField(text, "description");
        string shortCaption = ExtractJsonStringField(text, "short_caption");
        string[] tags = ExtractJsonStringArrayField(text, "tags");
        string setting = ExtractJsonStringField(text, "setting");
        string[] qualityFlags = ExtractJsonStringArrayField(text, "quality_flags");
        int peopleCount = ExtractJsonIntField(text, "people_count") ?? EstimatePeopleCountFromMalformedField(text);
        double confidence = ExtractJsonDoubleField(text, "confidence") ?? 0.0;

        bool hasUsefulContent =
            !string.IsNullOrWhiteSpace(description) ||
            !string.IsNullOrWhiteSpace(shortCaption) ||
            tags.Length > 0;

        if (!hasUsefulContent)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = shortCaption;
        }

        if (string.IsNullOrWhiteSpace(shortCaption))
        {
            shortCaption = description.Length > 90 ? description[..90].TrimEnd() : description;
        }

        if (string.IsNullOrWhiteSpace(setting))
        {
            setting = "unknown";
        }

        return new PhotoAnalysis
        {
            Description = description,
            ShortCaption = shortCaption,
            Tags = tags,
            PeopleCount = peopleCount,
            Setting = setting,
            QualityFlags = qualityFlags,
            Confidence = confidence
        };
    }

    private static string ExtractJsonStringField(string text, string fieldName)
    {
        Match match = Regex.Match(
            text,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? UnescapeJsonString(match.Groups["value"].Value).Trim() : "";
    }

    private static string[] ExtractJsonStringArrayField(string text, string fieldName)
    {
        Match arrayMatch = Regex.Match(
            text,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*\\[(?<items>.*?)\\]",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!arrayMatch.Success)
        {
            return [];
        }

        return Regex.Matches(arrayMatch.Groups["items"].Value, "\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"")
            .Select(match => UnescapeJsonString(match.Groups["value"].Value).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int? ExtractJsonIntField(string text, string fieldName)
    {
        Match match = Regex.Match(
            text,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*(?<value>\\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success && int.TryParse(match.Groups["value"].Value, out int value) ? value : null;
    }

    private static double? ExtractJsonDoubleField(string text, string fieldName)
    {
        Match match = Regex.Match(
            text,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*(?<value>\\d+(?:\\.\\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success && double.TryParse(match.Groups["value"].Value, out double value) ? value : null;
    }

    private static int EstimatePeopleCountFromMalformedField(string text)
    {
        Match peopleField = Regex.Match(
            text,
            "\\\"people_count\\\"\\s*:\\s*\\[(?<value>.*?)\\\"setting\\\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!peopleField.Success)
        {
            return 0;
        }

        return Regex.Matches(peopleField.Groups["value"].Value, "\\\"description\\\"", RegexOptions.IgnoreCase).Count;
    }

    private static string UnescapeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\\\"{value}\\\"") ?? value;
        }
        catch
        {
            return value
                .Replace("\\\"", "\"")
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\\t", " ");
        }
    }

    public static string ExtractJsonObject(string text)
    {
        string trimmed = text.Trim();

        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..].Trim();
        }
        else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[3..].Trim();
        }

        if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3].Trim();
        }

        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    public static PhotoAnalysis? EnrichAnalysisTags(PhotoAnalysis? analysis)
    {
        if (analysis is null)
        {
            return null;
        }

        string[] enrichedTags = BuildEnrichedTags(analysis);

        return new PhotoAnalysis
        {
            Description = analysis.Description,
            ShortCaption = analysis.ShortCaption,
            Tags = enrichedTags,
            PeopleCount = analysis.PeopleCount,
            Setting = analysis.Setting,
            QualityFlags = analysis.QualityFlags,
            Confidence = analysis.Confidence
        };
    }

    private static string[] BuildEnrichedTags(PhotoAnalysis analysis)
    {
        var orderedTags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTag(string? rawTag)
        {
            string tag = NormalizeTag(rawTag);
            if (string.IsNullOrWhiteSpace(tag) || IsJunkTag(tag))
            {
                return;
            }

            if (seen.Add(tag))
            {
                orderedTags.Add(tag);
            }
        }

        foreach (string tag in analysis.Tags)
        {
            AddTag(tag);
        }

        foreach (string phrase in ExtractUsefulPhrases(analysis.Description))
        {
            AddTag(phrase);
        }

        foreach (string phrase in ExtractUsefulPhrases(analysis.ShortCaption))
        {
            AddTag(phrase);
        }

        if (analysis.PeopleCount > 0)
        {
            AddTag("people");
        }

        if (analysis.PeopleCount == 1)
        {
            AddTag("person");
        }

        return PostProcessTags(orderedTags).Take(10).ToArray();
    }

    private static IEnumerable<string> PostProcessTags(IEnumerable<string> tags)
    {
        var orderedTags = tags
            .Select(NormalizeTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Where(tag => !IsJunkTag(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        orderedTags = CollapseRedundantHumanTags(orderedTags).ToList();

        return orderedTags;
    }

    private static IEnumerable<string> CollapseRedundantHumanTags(IEnumerable<string> tags)
    {
        var tagList = tags.ToList();
        bool hasSpecificHumanTag = tagList.Any(tag => SpecificHumanTags().Contains(tag));

        if (hasSpecificHumanTag)
        {
            return tagList.Where(tag => tag != "person" && tag != "people");
        }

        if (tagList.Contains("person", StringComparer.OrdinalIgnoreCase))
        {
            return tagList.Where(tag => tag != "people");
        }

        return tagList;
    }

    private static IEnumerable<string> ExtractUsefulPhrases(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (string phrase in KnownUsefulPhrases())
        {
            if (Regex.IsMatch(text, $"\\b{Regex.Escape(phrase)}\\b", RegexOptions.IgnoreCase))
            {
                yield return phrase;
            }
        }

        foreach (Match quoted in Regex.Matches(text, "['\"](?<value>[A-Za-z][A-Za-z0-9 &.-]{1,40})['\"]"))
        {
            yield return quoted.Groups["value"].Value;
        }

        foreach (Match capitalized in Regex.Matches(text, "\\b(?<value>[A-Z][A-Za-z0-9&.-]{2,})\\b"))
        {
            string value = capitalized.Groups["value"].Value;
            if (!IsSentenceStarterOnly(value))
            {
                yield return value;
            }
        }

        foreach (Match word in Regex.Matches(text.ToLowerInvariant(), "\\b[a-z][a-z0-9-]{2,}\\b"))
        {
            string value = word.Value;
            if (UsefulSingleWordTags().Contains(value))
            {
                yield return value;
            }
        }
    }

    private static string NormalizeTag(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return "";
        }

        string tag = rawTag.Trim().Trim(',', '.', ';', ':', '!', '?', '\'', '"', '(', ')', '[', ']');
        tag = Regex.Replace(tag, "\\s+", " ");
        tag = tag.ToLowerInvariant();

        if (TagAliases().TryGetValue(tag, out string? alias))
        {
            return alias;
        }

        if (tag.EndsWith("s", StringComparison.OrdinalIgnoreCase) && tag.Length > 4 && !tag.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            string singular = tag[..^1];
            tag = TagAliases().TryGetValue(singular, out string? singularAlias) ? singularAlias : singular;
        }

        return tag;
    }

    private static bool IsJunkTag(string tag)
    {
        return JunkTags().Contains(tag) || NoisyInferredTags().Contains(tag) || tag.Length < 3 || tag.Length > 40;
    }

    private static bool IsSentenceStarterOnly(string value)
    {
        return SentenceStarterWords().Contains(value.ToLowerInvariant());
    }

    private static HashSet<string> JunkTags() => new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "photo", "picture", "object", "objects", "item", "items", "thing", "things",
        "scene", "environment", "area", "view", "visible", "showing", "appears", "including",
        "near", "beside", "with", "without", "none", "unknown", "factual", "caption", "detail", "details",
        "wall"
    };

    private static HashSet<string> NoisyInferredTags() => new(StringComparer.OrdinalIgnoreCase)
    {
        "affection", "affectionate", "celebration", "event", "fun", "happiness", "holiday", "leisure",
        "lifestyle", "posing", "relaxation", "travel", "vacation"
    };

    private static Dictionary<string, string> TagAliases() => new(StringComparer.OrdinalIgnoreCase)
    {
        { "automobile", "automotive" },
        { "automobiles", "automotive" },
        { "christma", "christmas" },
        { "cloud", "clouds" },
        { "horse", "horse" },
        { "horses", "horse" },
        { "outdoors", "outdoor" },
        { "vehicle", "car" }
    };

    private static HashSet<string> SpecificHumanTags() => new(StringComparer.OrdinalIgnoreCase)
    {
        "baby", "child", "children", "girl", "man", "woman"
    };

    private static HashSet<string> SentenceStarterWords() => new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "two", "three", "four", "five", "several", "one"
    };

    private static HashSet<string> UsefulSingleWordTags() => new(StringComparer.OrdinalIgnoreCase)
    {
        "airplane", "animal", "appliance", "automotive", "baby", "backyard", "ball", "bar", "barn",
        "baseball", "basket", "bathroom", "beach", "beagle", "bed", "bedroom", "bench", "bicycle",
        "bike", "bird", "birthday", "boat", "book", "bottle", "box", "bridge", "building", "bus",
        "cabinet", "cake", "camera", "camping", "car", "cat", "chair", "child", "children", "christmas",
        "city", "clock", "cloud", "computer", "concert", "couch", "counter", "desk", "document", "dog",
        "door", "driveway", "family", "fence", "field", "fire", "flower", "food", "football", "forest",
        "garage", "garden", "grass", "guitar", "house", "indoor", "kitchen", "lake", "lamp", "landscape",
        "man", "mirror", "motorcycle", "mountain", "outdoor", "park", "party", "patio", "person", "people",
        "pet", "pine", "plant", "pool", "portrait", "river", "road", "room", "screen", "sign", "sky", "snow",
        "sofa", "sports", "street", "sunset", "swimming", "table", "television", "tree", "truck", "valvoline",
        "wall", "water", "wedding", "window", "woman", "workshop", "yard"
    };

    private static string[] KnownUsefulPhrases() =>
    [
        "covered car", "front loader", "garage sign", "valvoline sign", "license plate", "picnic table",
        "dining table", "coffee table", "pine cone", "blue sky", "automotive banner", "automotive banners",
        "kitchen counter", "living room", "dining room", "bedroom", "family room", "swimming pool",
        "hot tub", "front yard", "back yard", "parking lot", "race car", "classic car", "sports car",
        "vintage car"
    ];

    public static string BuildImmichXmp(PhotoAnalysis analysis, bool addTags = false)
    {
        XNamespace x = "adobe:ns:meta/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace xmp = "http://ns.adobe.com/xap/1.0/";
        XNamespace tiff = "http://ns.adobe.com/tiff/1.0/";
        XNamespace digiKam = "http://www.digikam.org/ns/1.0/";
        XNamespace lr = "http://ns.adobe.com/lightroom/1.0/";

        string title = string.IsNullOrWhiteSpace(analysis.ShortCaption)
            ? analysis.Description
            : analysis.ShortCaption;

        string description = analysis.Description;

        string[] tags = analysis.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Where(tag => !string.Equals(tag, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var descriptionElements = new List<XElement>
        {
            new(dc + "title",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        title
                    )
                )
            ),

            new(dc + "description",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li",
                        new XAttribute(XNamespace.Xml + "lang", "x-default"),
                        description
                    )
                )
            ),

            new(tiff + "ImageDescription", description)
        };

        if (addTags && tags.Length > 0)
        {
            descriptionElements.AddRange(
            [
                new XElement(dc + "subject",
                    new XElement(rdf + "Bag",
                        tags.Select(tag => new XElement(rdf + "li", tag))
                    )
                ),

                new XElement(digiKam + "TagsList",
                    new XElement(rdf + "Seq",
                        tags.Select(tag => new XElement(rdf + "li", tag))
                    )
                ),

                new XElement(lr + "HierarchicalSubject",
                    new XElement(rdf + "Bag",
                        tags.Select(tag => new XElement(rdf + "li", tag))
                    )
                )
            ]);
        }

        descriptionElements.Add(new XElement(xmp + "MetadataDate", DateTimeOffset.UtcNow.ToString("O")));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(x + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", rdf),
                    new XElement(rdf + "Description",
                        new XAttribute(XNamespace.Xmlns + "dc", dc),
                        new XAttribute(XNamespace.Xmlns + "xmp", xmp),
                        new XAttribute(XNamespace.Xmlns + "tiff", tiff),
                        new XAttribute(XNamespace.Xmlns + "digiKam", digiKam),
                        new XAttribute(XNamespace.Xmlns + "lr", lr),
                        descriptionElements
                    )
                )
            )
        );

        return document.Declaration + Environment.NewLine + document.ToString(SaveOptions.None) + Environment.NewLine;
    }

    public static string GetPhotoAiSidecarPath(string imagePath)
    {
        string? directory = Path.GetDirectoryName(imagePath);
        if (directory is null)
        {
            throw new InvalidOperationException($"Could not determine directory for: {imagePath}");
        }

        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imagePath);
        return Path.Combine(directory, $"{fileNameWithoutExtension}.photoai.json");
    }

    public static string GetImmichXmpSidecarPath(string imagePath) => imagePath + ".xmp";

    public static IReadOnlyList<string> GetSupportedImagePaths(string rootPath, bool recursive)
    {
        string normalizedRoot = NormalizeDirectoryPath(rootPath);
        IEnumerable<string> paths = recursive
            ? EnumerateFilesRecursivelySafely(normalizedRoot)
            : EnumerateTopLevelFilesSafely(normalizedRoot);

        return paths
            .Where(path => !IsInExcludedScanDirectory(path, normalizedRoot))
            .Where(path => PhotoAiDefaults.SupportedExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateTopLevelFilesSafely(string rootPath)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (IsRecoverableEnumerationException(ex))
        {
            yield break;
        }

        foreach (string file in files)
        {
            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursivelySafely(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            string currentDirectory = pending.Pop();
            if (!string.Equals(NormalizeDirectoryPath(currentDirectory), NormalizeDirectoryPath(rootPath), StringComparison.OrdinalIgnoreCase) &&
                IsExcludedScanDirectoryPath(currentDirectory, rootPath))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory, "*.*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (IsRecoverableEnumerationException(ex))
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (IsRecoverableEnumerationException(ex))
            {
                continue;
            }

            foreach (string directory in directories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsExcludedScanDirectoryPath(directory, rootPath))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsRecoverableEnumerationException(Exception ex) =>
        ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException or PathTooLongException;

    public static bool IsInExcludedScanDirectory(string candidatePath, string rootPath)
    {
        string fullCandidate = NormalizeDirectoryOrFilePath(candidatePath);
        string fullRoot = NormalizeDirectoryPath(rootPath);

        if (!IsPathUnderRoot(fullCandidate, fullRoot))
        {
            return false;
        }

        string relativePath = Path.GetRelativePath(fullRoot, fullCandidate);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return false;
        }

        string[] parts = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(IsExcludedScanDirectoryName);
    }

    public static bool IsExcludedScanDirectoryPath(string candidateDirectoryPath, string rootPath)
    {
        string fullCandidate = NormalizeDirectoryPath(candidateDirectoryPath);
        string fullRoot = NormalizeDirectoryPath(rootPath);

        if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) || !IsPathUnderRoot(fullCandidate, fullRoot))
        {
            return false;
        }

        string relativePath = Path.GetRelativePath(fullRoot, fullCandidate);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return false;
        }

        string[] parts = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Any(IsExcludedScanDirectoryName);
    }

    private static bool IsExcludedScanDirectoryName(string directoryName) =>
        string.Equals(directoryName, ".photoai", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(directoryName, ".Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(directoryName, "@eaDir", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(directoryName, "thumbs", StringComparison.OrdinalIgnoreCase);

    public static bool IsPathUnderRoot(string candidatePath, string rootPath)
    {
        string fullCandidate = NormalizeDirectoryOrFilePath(candidatePath);
        string fullRoot = NormalizeDirectoryPath(rootPath);

        if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRunLogPath(string rootPath)
    {
        string logDirectory = Path.Combine(rootPath, ".photoai");
        Directory.CreateDirectory(logDirectory);

        string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(logDirectory, $"photoai-run-{timestamp}.log");
    }

    private static async Task AppendRunLogAsync(string logPath, string eventName, IEnumerable<string> details, CancellationToken cancellationToken = default)
    {
        string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

        List<string> lines =
        [
            $"[{timestamp}] {eventName}"
        ];

        lines.AddRange(details.Select(detail => $"  {detail}"));
        lines.Add(string.Empty);

        await File.AppendAllLinesAsync(logPath, lines, cancellationToken);
    }

    private static string OneLineExcerpt(string text, int maxLength)
    {
        string oneLine = text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();

        while (oneLine.Contains("  ", StringComparison.Ordinal))
        {
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (oneLine.Length <= maxLength)
        {
            return oneLine;
        }

        return oneLine[..maxLength] + "...";
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeDirectoryOrFilePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string FormatServerAddress(string ollamaBaseUrl)
    {
        if (Uri.TryCreate(ollamaBaseUrl, UriKind.Absolute, out Uri? uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }

        return ollamaBaseUrl;
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "n/a"
            : $"{timestamp.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} {FormatLocalTimeZoneAbbreviation(timestamp.Value)}";
    }

    private static string FormatMaxImageDimension(int maxImageDimensionPixels)
    {
        return maxImageDimensionPixels <= 0
            ? "original/full-res"
            : $"{maxImageDimensionPixels}px longest edge";
    }

    private static string FormatLocalTimeZoneAbbreviation(DateTimeOffset timestamp)
    {
        TimeZoneInfo local = TimeZoneInfo.Local;
        string displayName = local.IsDaylightSavingTime(timestamp) ? local.DaylightName : local.StandardName;
        if (displayName.Contains("Mountain", StringComparison.OrdinalIgnoreCase))
        {
            return "MST";
        }

        string abbreviation = string.Concat(displayName
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 0 && char.IsLetter(part[0]))
            .Select(part => char.ToUpperInvariant(part[0])));
        return string.IsNullOrWhiteSpace(abbreviation) ? local.Id : abbreviation;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FormatAverageSecondsPerPhoto(TimeSpan duration)
    {
        double seconds = duration < TimeSpan.Zero ? 0.0 : duration.TotalSeconds;
        return $"{seconds:F1} seconds";
    }

    private static string[] BuildRunSummaryLines(PhotoAiScanSummary summary)
    {
        return
        [
            $"Start: {FormatTimestamp(summary.StartTime)}",
            $"Stop: {FormatTimestamp(summary.StopTime)}",
            $"Elapsed: {FormatDuration(summary.ElapsedTime)}",
            $"Average/photo: {FormatAverageSecondsPerPhoto(summary.AverageTimePerProcessedPhoto)}",
            $"Files processed: {summary.Completed}",
            $"XMP files successfully written: {summary.XmpWritten}",
            $"Skipped: {summary.Skipped}",
            $"Failed: {summary.Failed}",
            $"Parse/XMP skipped: {summary.ParseFailed}",
            $"Model failures: {summary.ModelFailures}",
            $"Primary retry attempts: {summary.PrimaryRetryAttempts}",
            $"Primary retry successes: {summary.PrimaryRetrySucceeded}",
            $"Primary retry failures: {summary.PrimaryRetryFailed}",
            $"Fallback attempts: {summary.FallbackAttempts}",
            $"Fallback successes: {summary.FallbackSucceeded}"
        ];
    }

    private static string[] BuildDryRunSummaryLines(PhotoAiScanSummary summary)
    {
        return
        [
            $"Start: {FormatTimestamp(summary.StartTime)}",
            $"Stop: {FormatTimestamp(summary.StopTime)}",
            $"Elapsed: {FormatDuration(summary.ElapsedTime)}",
            $"Average/photo: {FormatAverageSecondsPerPhoto(summary.AverageTimePerProcessedPhoto)}",
            $"Would skip files: {summary.Skipped}",
            $"Would process: {summary.WouldProcess}",
            $"Would write JSON files: {summary.WouldWriteJsonSidecar}",
            $"Would write XMP files: {summary.WouldWriteXmpSidecar}",
            $"Would skip JSON writes: {summary.WouldSkipJsonSidecar}",
            $"Would skip XMP writes: {summary.WouldSkipXmpSidecar}",
            $"Existing JSON: {summary.ExistingJsonSidecars}",
            $"Existing XMP: {summary.ExistingXmpSidecars}",
            $"Blocked/failed: {summary.Failed}"
        ];
    }

    private static string FormatProgressLabel(
        PhotoAiScanOptions options,
        int localIndex,
        int localTotal,
        PhotoAiScanSummary summary,
        bool includeCurrentFile)
    {
        if (options.ProgressTotalFiles is not > 0 || options.ProgressTotalFiles.Value == localTotal)
        {
            return $"{localIndex}/{localTotal}";
        }

        int priorFoldersFinished =
            Math.Max(0, options.ProgressCompletedOffset) +
            Math.Max(0, options.ProgressSkippedOffset) +
            Math.Max(0, options.ProgressFailedOffset);
        int currentFolderFinished =
            (summary.DryRun ? summary.WouldProcess : summary.Completed) +
            summary.Skipped +
            summary.Failed;
        int aggregatePosition = priorFoldersFinished + currentFolderFinished + (includeCurrentFile ? 1 : 0);
        aggregatePosition = Math.Clamp(aggregatePosition, 0, options.ProgressTotalFiles.Value);

        return $"batch {aggregatePosition}/{options.ProgressTotalFiles.Value}, folder {localIndex}/{localTotal}";
    }

    private static PhotoAiRunProgressSnapshot BuildProgressSnapshot(
        PhotoAiScanSummary summary,
        PhotoAiScanOptions options,
        PhotoAiRunState state,
        string phase,
        int? totalFiles)
    {
        int progressOffset = Math.Max(0, options.ProgressCompletedOffset);
        int completedFiles = progressOffset + (summary.DryRun ? summary.WouldProcess : summary.Completed);
        int skippedFiles = Math.Max(0, options.ProgressSkippedOffset) + summary.Skipped;
        int failedFiles = Math.Max(0, options.ProgressFailedOffset) + summary.Failed;
        int primaryRetryAttempts = Math.Max(0, options.ProgressPrimaryRetryAttemptsOffset) + summary.PrimaryRetryAttempts;
        int fallbackAttempts = Math.Max(0, options.ProgressFallbackAttemptsOffset) + summary.FallbackAttempts;
        int? effectiveTotalFiles = options.ProgressTotalFiles ?? totalFiles;
        DateTimeOffset startedAt = options.ProgressStartedAt ?? summary.StartTime;

        return PhotoAiRunProgressSnapshot.Create(
            state,
            phase,
            startedAt,
            DateTimeOffset.Now,
            effectiveTotalFiles,
            completedFiles,
            skippedFiles,
            failedFiles,
            primaryRetryAttempts,
            fallbackAttempts);
    }

    private static void Report(
        IProgress<PhotoAiScanProgress>? progress,
        string eventName,
        string message,
        string? imagePath = null,
        PhotoAiRunProgressSnapshot? snapshot = null)
    {
        progress?.Report(new PhotoAiScanProgress
        {
            EventName = eventName,
            Message = message,
            ImagePath = imagePath,
            Snapshot = snapshot
        });
    }
}
