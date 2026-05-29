using PhotoAIApp.Core;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace ImmichTagger.Server;

public sealed class ScanJobService
{
    private readonly PhotoAiScanner _scanner = new();
    private readonly object _gate = new();
    private readonly ConcurrentQueue<string> _recentLogLines = new();
    private CancellationTokenSource? _activeCancellation;
    private PhotoAiPauseController? _activePauseController;
    private Task? _activeTask;
    private ScanJobStatus _status = ScanJobStatus.Idle();

    public ScanJobStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status with { RecentLogLines = _recentLogLines.ToArray() };
            }
        }
    }

    public bool TryStart(ImmichTaggerSettings settings, ScanStartRequest request, bool dryRun, out string message)
    {
        lock (_gate)
        {
            if (_activeTask is { IsCompleted: false })
            {
                message = "A scan is already running.";
                return false;
            }

            _activeCancellation?.Dispose();
            _activeCancellation = new CancellationTokenSource();
            _activePauseController = new PhotoAiPauseController();
            _recentLogLines.Clear();

            string[] selectedFolders = GetSelectedFolders(request, settings.PhotoRoot);
            string startMessage = selectedFolders.Length > 1
                ? (dryRun ? $"Dry run started for {selectedFolders.Length} selected folders." : $"Live scan started for {selectedFolders.Length} selected folders.")
                : (dryRun ? "Dry run started." : "Live scan started.");

            _status = new ScanJobStatus(
                IsRunning: true,
                IsDryRun: dryRun,
                IsPaused: false,
                StartedAt: DateTimeOffset.Now,
                FinishedAt: null,
                FolderPath: selectedFolders[0],
                SelectedFolderPaths: selectedFolders,
                Message: startMessage,
                LastProgress: null,
                Summary: null,
                RunLogPath: null,
                Error: null,
                RecentLogLines: []);

            ImmichTaggerSettings runSettings = CloneSettings(settings);
            CancellationToken token = _activeCancellation.Token;
            _activeTask = Task.Run(() => RunScanAsync(runSettings, request with { FolderPath = selectedFolders[0], FolderPaths = selectedFolders }, dryRun, token), CancellationToken.None);
            message = _status.Message;
            return true;
        }
    }

    public bool Pause(out string message)
    {
        lock (_gate)
        {
            if (_activeTask is not { IsCompleted: false } || _activePauseController is null)
            {
                message = "No active scan to pause.";
                return false;
            }

            if (_activePauseController.IsPaused)
            {
                message = "Scan is already paused.";
                return false;
            }

            _activePauseController.Pause();
            _status = _status with
            {
                IsPaused = true,
                Message = "Pause requested. Current Ollama call/file write will finish, then scanning will pause before the next image."
            };
            EnqueueRecentLogLine(_status.Message);
            message = _status.Message;
            return true;
        }
    }

    public bool Resume(out string message)
    {
        lock (_gate)
        {
            if (_activeTask is not { IsCompleted: false } || _activePauseController is null)
            {
                message = "No active scan to resume.";
                return false;
            }

            if (!_activePauseController.IsPaused)
            {
                message = "Scan is not paused.";
                return false;
            }

            _activePauseController.Resume();
            _status = _status with
            {
                IsPaused = false,
                Message = "Resume requested. The scan will continue at the next pause checkpoint."
            };
            EnqueueRecentLogLine(_status.Message);
            message = _status.Message;
            return true;
        }
    }

    public bool Cancel(out string message)
    {
        lock (_gate)
        {
            if (_activeTask is not { IsCompleted: false } || _activeCancellation is null)
            {
                message = "No active scan to cancel.";
                return false;
            }

            _activeCancellation.Cancel();
            message = "Cancellation requested.";
            return true;
        }
    }

    private async Task RunScanAsync(ImmichTaggerSettings settings, ScanStartRequest request, bool dryRun, CancellationToken cancellationToken)
    {
        PhotoAiScanSummary? completedSummary = null;
        string[] selectedFolders = GetSelectedFolders(request, settings.PhotoRoot);

        try
        {
            var progress = new Progress<PhotoAiScanProgress>(OnProgress);
            var summaries = new List<PhotoAiScanSummary>();
            int? totalLimit = request.Limit;
            DateTimeOffset aggregateStartTime = DateTimeOffset.Now;
            int aggregateTotalFiles = CountAggregateCandidateImages(selectedFolders, settings.Recursive, null);
            int aggregateVisitedFiles = 0;
            int aggregateCompletedOffset = 0;
            int aggregateSkippedOffset = 0;
            int aggregateFailedOffset = 0;
            int aggregatePrimaryRetryAttemptsOffset = 0;
            int aggregateFallbackAttemptsOffset = 0;

            if (selectedFolders.Length > 1)
            {
                AppendStatusLine($"Queued {aggregateTotalFiles} image files across {selectedFolders.Length} selected folders.");
            }

            for (int index = 0; index < selectedFolders.Length; index++)
            {
                if (totalLimit is > 0 && aggregateVisitedFiles >= totalLimit.Value)
                {
                    break;
                }

                string folder = selectedFolders[index];
                int? folderLimit = totalLimit is > 0 ? totalLimit.Value - aggregateVisitedFiles : null;
                bool unloadModelsAfterFolder = index == selectedFolders.Length - 1 || totalLimit is > 0;
                AppendStatusLine(selectedFolders.Length == 1
                    ? $"Starting scan: {folder}"
                    : $"Starting folder {index + 1}/{selectedFolders.Length}: {folder}");

                PhotoAiScanSummary folderSummary = await _scanner.ScanFolderAsync(
                    CreateScanOptions(
                        settings,
                        folder,
                        dryRun,
                        _activePauseController,
                        folderLimit,
                        unloadModelsAfterFolder,
                        aggregateCompletedOffset,
                        aggregateSkippedOffset,
                        aggregateFailedOffset,
                        aggregatePrimaryRetryAttemptsOffset,
                        aggregateFallbackAttemptsOffset,
                        selectedFolders.Length > 1 ? aggregateTotalFiles : null,
                        selectedFolders.Length > 1 ? aggregateStartTime : null),
                    progress,
                    cancellationToken);

                summaries.Add(folderSummary);
                int folderVisitedFiles = CountVisitedFiles(folderSummary);
                aggregateVisitedFiles += folderVisitedFiles;
                aggregateCompletedOffset += folderSummary.DryRun ? folderSummary.WouldProcess : folderSummary.Completed;
                aggregateSkippedOffset += folderSummary.Skipped;
                aggregateFailedOffset += folderSummary.Failed;
                aggregatePrimaryRetryAttemptsOffset += folderSummary.PrimaryRetryAttempts;
                aggregateFallbackAttemptsOffset += folderSummary.FallbackAttempts;
            }

            PhotoAiScanSummary summary = summaries.Count == 1
                ? summaries[0]
                : PhotoAiScanSummary.Combine(summaries);

            if (summaries.Count > 1 && !summary.DryRun)
            {
                await AppendAggregateRunCompleteAsync(summary, summaries, cancellationToken);
            }

            completedSummary = summary;

            string finalMessage = dryRun ? "Dry run complete." : "Live scan complete.";
            string? finalError = null;

            if (!dryRun && settings.SyncImmich)
            {
                try
                {
                    string syncOutcome = await SyncImmichSidecarMetadataAsync(settings, cancellationToken);
                    finalMessage = $"Live scan complete. {syncOutcome}";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string syncFailure = $"Immich sync failed: {ex.Message}";
                    AppendStatusLine(syncFailure);
                    finalMessage = "Live scan complete. Immich sync failed.";
                    finalError = syncFailure;
                }
            }

            string? finalRunLogPath = summaries.LastOrDefault(item => !item.DryRun && File.Exists(item.RunLogPath))?.RunLogPath;

            lock (_gate)
            {
                _status = _status with
                {
                    IsRunning = false,
                    IsPaused = false,
                    FinishedAt = DateTimeOffset.Now,
                    FolderPath = selectedFolders[0],
                    SelectedFolderPaths = selectedFolders,
                    Message = finalMessage,
                    Summary = PhotoAiRunSummaryDocument.FromSummary(summary),
                    RunLogPath = dryRun ? null : finalRunLogPath ?? summary.RunLogPath,
                    Error = finalError
                };
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _status = _status with
                {
                    IsRunning = false,
                    IsPaused = false,
                    FinishedAt = DateTimeOffset.Now,
                    FolderPath = selectedFolders[0],
                    SelectedFolderPaths = selectedFolders,
                    Message = completedSummary is null ? "Scan cancelled." : "Live scan complete. Immich sync cancelled.",
                    Summary = completedSummary is null ? _status.Summary : PhotoAiRunSummaryDocument.FromSummary(completedSummary),
                    RunLogPath = completedSummary is null ? _status.RunLogPath : FirstExistingRunLogPath(completedSummary.RunLogPath, _status.RunLogPath),
                    Error = null
                };
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _status = _status with
                {
                    IsRunning = false,
                    IsPaused = false,
                    FinishedAt = DateTimeOffset.Now,
                    FolderPath = selectedFolders[0],
                    SelectedFolderPaths = selectedFolders,
                    Message = completedSummary is null ? "Scan failed." : "Live scan complete. Immich sync failed.",
                    Summary = completedSummary is null ? _status.Summary : PhotoAiRunSummaryDocument.FromSummary(completedSummary),
                    RunLogPath = completedSummary is null ? _status.RunLogPath : FirstExistingRunLogPath(completedSummary.RunLogPath, _status.RunLogPath),
                    Error = ex.Message
                };
            }
        }
    }

    private static string[] GetSelectedFolders(ScanStartRequest request, string defaultFolderPath)
    {
        IEnumerable<string> requestedFolders = request.FolderPaths is { Count: > 0 }
            ? request.FolderPaths
            : [string.IsNullOrWhiteSpace(request.FolderPath) ? defaultFolderPath : request.FolderPath];

        return requestedFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PhotoAiScanOptions CreateScanOptions(
        ImmichTaggerSettings settings,
        string folderPath,
        bool dryRun,
        PhotoAiPauseController? pauseController,
        int? limit,
        bool unloadModelsAtEnd,
        int completedOffset,
        int skippedOffset,
        int failedOffset,
        int primaryRetryAttemptsOffset,
        int fallbackAttemptsOffset,
        int? progressTotalFiles,
        DateTimeOffset? progressStartedAt)
    {
        PhotoAiScanOptions baseOptions = settings.ToScanOptions(folderPath, dryRun, limit);
        return new PhotoAiScanOptions
        {
            FolderPath = baseOptions.FolderPath,
            SafetyRootPath = baseOptions.SafetyRootPath,
            Recursive = baseOptions.Recursive,
            Force = baseOptions.Force,
            WriteJson = baseOptions.WriteJson,
            WriteXmp = baseOptions.WriteXmp,
            OverwriteJson = baseOptions.OverwriteJson,
            OverwriteXmp = baseOptions.OverwriteXmp,
            AddTags = baseOptions.AddTags,
            DryRun = baseOptions.DryRun,
            OllamaBaseUrl = baseOptions.OllamaBaseUrl,
            Model = baseOptions.Model,
            ModelPreference = baseOptions.ModelPreference,
            FallbackOllamaBaseUrl = baseOptions.FallbackOllamaBaseUrl,
            FallbackModel = baseOptions.FallbackModel,
            PauseController = pauseController,
            MaxImageDimensionPixels = baseOptions.MaxImageDimensionPixels,
            FallbackMaxImageDimensionPixels = baseOptions.FallbackMaxImageDimensionPixels,
            Limit = baseOptions.Limit,
            UnloadModelsAtEnd = unloadModelsAtEnd,
            ProgressCompletedOffset = completedOffset,
            ProgressSkippedOffset = skippedOffset,
            ProgressFailedOffset = failedOffset,
            ProgressPrimaryRetryAttemptsOffset = primaryRetryAttemptsOffset,
            ProgressFallbackAttemptsOffset = fallbackAttemptsOffset,
            ProgressTotalFiles = progressTotalFiles,
            ProgressStartedAt = progressStartedAt
        };
    }

    private static int CountAggregateCandidateImages(IEnumerable<string> selectedFolders, bool recursive, int? totalLimit)
    {
        int total = 0;
        foreach (string folder in selectedFolders)
        {
            int? remainingLimit = totalLimit is > 0 ? totalLimit.Value - total : null;
            if (remainingLimit is <= 0)
            {
                break;
            }

            total += CountCandidateImages(folder, recursive, remainingLimit);
        }

        return total;
    }

    private static int CountCandidateImages(string folderPath, bool recursive, int? limit)
    {
        int count = 0;
        foreach (string path in PhotoAiScanner.GetSupportedImagePaths(folderPath, recursive))
        {
            _ = path;
            count++;
            if (limit is > 0 && count >= limit.Value)
            {
                break;
            }
        }

        return count;
    }

    private static int CountVisitedFiles(PhotoAiScanSummary summary)
    {
        return summary.DryRun
            ? summary.WouldProcess
            : summary.Completed + summary.Failed;
    }

    private static async Task AppendAggregateRunCompleteAsync(PhotoAiScanSummary summary, IReadOnlyList<PhotoAiScanSummary> folderSummaries, CancellationToken cancellationToken)
    {
        string? runLogPath = folderSummaries.LastOrDefault(item => !item.DryRun && File.Exists(item.RunLogPath))?.RunLogPath;
        if (string.IsNullOrWhiteSpace(runLogPath))
        {
            return;
        }

        string[] lines =
        [
            string.Empty,
            $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] AGGREGATE RUN COMPLETE",
            $"  selected_folders={folderSummaries.Count}",
            $"  images_found={summary.ImagesFound}",
            $"  start_time={FormatSummaryTimestamp(summary.StartTime)}",
            $"  stop_time={FormatSummaryTimestamp(summary.StopTime)}",
            $"  elapsed={FormatSummaryDuration(summary.ElapsedTime)}",
            $"  average_time_per_processed_photo={FormatAverageSecondsPerPhoto(summary.AverageTimePerProcessedPhoto)}",
            $"  completed={summary.Completed}",
            $"  skipped={summary.Skipped}",
            $"  failed={summary.Failed}",
            $"  parse_xmp_skipped={summary.ParseFailed}",
            $"  xmp_written={summary.XmpWritten}",
            $"  json_write_skipped={summary.JsonWriteSkipped}",
            $"  xmp_write_skipped={summary.XmpWriteSkipped}",
            $"  model_failures={summary.ModelFailures}",
            $"  primary_retry_attempts={summary.PrimaryRetryAttempts}",
            $"  primary_retry_successes={summary.PrimaryRetrySucceeded}",
            $"  primary_retry_failures={summary.PrimaryRetryFailed}",
            $"  fallback_attempts={summary.FallbackAttempts}",
            $"  fallback_successes={summary.FallbackSucceeded}"
        ];

        await File.AppendAllTextAsync(runLogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, cancellationToken);
    }

    private static string FormatSummaryTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "n/a"
            : $"{timestamp.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} {FormatLocalTimeZoneAbbreviation(timestamp.Value)}";
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

    private static string FormatSummaryDuration(TimeSpan duration)
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

    private async Task<string> SyncImmichSidecarMetadataAsync(ImmichTaggerSettings settings, CancellationToken cancellationToken)
    {
        ImmichSyncConfig config = LoadImmichSyncConfig(settings);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            string skipped = "Immich sync skipped: missing Immich API key.";
            AppendStatusLine(skipped);
            return skipped;
        }

        using HttpClient http = CreateImmichHttpClient(config);

        AppendStatusLine("Sync Immich: checking sidecar job...");
        await WaitForImmichSidecarQueueIdleAsync(http, "before discover", cancellationToken);

        AppendStatusLine("Sync Immich: Discover sidecar metadata...");
        await RunImmichSidecarQueueCommandAsync(http, force: false, cancellationToken);

        AppendStatusLine("Sync Immich: waiting for Discover to finish...");
        await WaitForImmichSidecarQueueIdleAsync(http, "after discover", cancellationToken);

        AppendStatusLine("Sync Immich: Sync sidecar metadata...");
        await RunImmichSidecarQueueCommandAsync(http, force: true, cancellationToken);

        AppendStatusLine("Sync Immich requests sent.");
        return "Immich sync complete.";
    }

    private static string? FirstExistingRunLogPath(params string?[] candidates)
    {
        foreach (string? candidateBlock in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidateBlock))
            {
                continue;
            }

            foreach (string candidate in candidateBlock
                .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static ImmichSyncConfig LoadImmichSyncConfig(ImmichTaggerSettings settings)
    {
        return new ImmichSyncConfig(
            BaseUrl: (settings.ImmichBaseUrl ?? "http://192.168.1.8:2283").Trim().TrimEnd('/'),
            ApiKey: settings.ImmichApiKey?.Trim() ?? string.Empty);
    }

    private static HttpClient CreateImmichHttpClient(ImmichSyncConfig config)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        http.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
        return http;
    }

    private async Task RunImmichSidecarQueueCommandAsync(HttpClient http, bool force, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var payload = JsonContent.Create(new
            {
                command = "start",
                force
            });

            using HttpResponseMessage response = await http.PutAsync("/api/jobs/sidecar", payload, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if ((int)response.StatusCode == 400 && responseBody.Contains("Job is already running", StringComparison.OrdinalIgnoreCase))
            {
                string operation = force ? "sync" : "discover";
                AppendStatusLine($"Sync Immich: sidecar job already running; waiting before retrying {operation}...");
                await WaitForImmichSidecarQueueIdleAsync(http, $"retry {operation}", cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            throw new InvalidOperationException($"Immich sidecar {(force ? "sync" : "discover")} request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
        }

        throw new InvalidOperationException($"Immich sidecar {(force ? "sync" : "discover")} request failed: sidecar job was still running after waiting and retrying.");
    }

    private async Task WaitForImmichSidecarQueueIdleAsync(HttpClient http, string context, CancellationToken cancellationToken)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(10);
        TimeSpan delay = TimeSpan.FromSeconds(5);
        DateTimeOffset deadline = DateTimeOffset.Now + timeout;
        bool loggedWaiting = false;

        while (true)
        {
            ImmichSidecarQueueSnapshot snapshot = await GetImmichSidecarQueueSnapshotAsync(http, cancellationToken);
            if (!snapshot.IsBusy)
            {
                if (loggedWaiting)
                {
                    AppendStatusLine($"Sync Immich: sidecar job is idle ({context}).");
                }

                return;
            }

            if (!loggedWaiting)
            {
                string details = $"active={snapshot.Active}, waiting={snapshot.Waiting}, delayed={snapshot.Delayed}, paused={snapshot.Paused}, queue_active={snapshot.QueueIsActive}";
                AppendStatusLine($"Sync Immich: sidecar job is already running; waiting... ({details})");
                loggedWaiting = true;
            }

            if (DateTimeOffset.Now >= deadline)
            {
                throw new InvalidOperationException($"Immich sidecar job was still busy after {timeout.TotalMinutes:0} minutes while waiting {context}.");
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static async Task<ImmichSidecarQueueSnapshot> GetImmichSidecarQueueSnapshotAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync("/api/jobs", cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Immich jobs status request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("sidecar", out JsonElement sidecar))
        {
            throw new InvalidOperationException("Immich jobs status response did not include a sidecar queue.");
        }

        bool queueIsActive = false;
        bool queueIsPaused = false;
        if (sidecar.TryGetProperty("queueStatus", out JsonElement queueStatus))
        {
            queueIsActive = GetBooleanProperty(queueStatus, "isActive");
            queueIsPaused = GetBooleanProperty(queueStatus, "isPaused");
        }

        int active = 0;
        int waiting = 0;
        int delayed = 0;
        int paused = 0;
        if (sidecar.TryGetProperty("jobCounts", out JsonElement jobCounts))
        {
            active = GetIntProperty(jobCounts, "active");
            waiting = GetIntProperty(jobCounts, "waiting");
            delayed = GetIntProperty(jobCounts, "delayed");
            paused = GetIntProperty(jobCounts, "paused");
        }

        return new ImmichSidecarQueueSnapshot(queueIsActive, queueIsPaused, active, waiting, delayed, paused);
    }

    private static bool GetBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private void OnProgress(PhotoAiScanProgress progress)
    {
        EnqueueRecentLogLine(progress.Message);

        lock (_gate)
        {
            _status = _status with
            {
                Message = progress.Message,
                IsPaused = progress.Snapshot?.State == PhotoAiRunState.Paused,
                LastProgress = progress,
                RunLogPath = ExtractRunLogPath(progress.Message) ?? _status.RunLogPath
            };
        }
    }

    private void AppendStatusLine(string message)
    {
        EnqueueRecentLogLine(message);

        lock (_gate)
        {
            _status = _status with
            {
                Message = message
            };
        }
    }

    private void EnqueueRecentLogLine(string message)
    {
        _recentLogLines.Enqueue(message);
        while (_recentLogLines.Count > 25 && _recentLogLines.TryDequeue(out _))
        {
        }
    }

    private static string? ExtractRunLogPath(string? message)
    {
        const string prefix = "Anomaly log:";
        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = message[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(path) || path.StartsWith("(", StringComparison.Ordinal) ? null : path;
    }

    private static ImmichTaggerSettings CloneSettings(ImmichTaggerSettings settings)
    {
        return new ImmichTaggerSettings
        {
            PhotoRoot = settings.PhotoRoot,
            ConfigRoot = settings.ConfigRoot,
            LogRoot = settings.LogRoot,
            DefaultFolderPath = settings.DefaultFolderPath,
            Recursive = settings.Recursive,
            Force = settings.Force,
            WriteJson = settings.WriteJson,
            WriteXmp = settings.WriteXmp,
            AddTags = settings.AddTags,
            DryRunDefault = settings.DryRunDefault,
            OverwriteSidecars = settings.OverwriteSidecars,
            Limit = settings.Limit,
            PrimaryOllamaUrl = settings.PrimaryOllamaUrl,
            PrimaryModel = settings.PrimaryModel,
            MaxImageSize = settings.MaxImageSize,
            FallbackEnabled = settings.FallbackEnabled,
            FallbackOllamaUrl = settings.FallbackOllamaUrl,
            FallbackModel = settings.FallbackModel,
            FallbackMaxImageSize = settings.FallbackMaxImageSize,
            SyncImmich = settings.SyncImmich,
            ImmichBaseUrl = settings.ImmichBaseUrl,
            ImmichApiKey = settings.ImmichApiKey
        };
    }
}

public sealed record ScanStartRequest(string? FolderPath = null, IReadOnlyList<string>? FolderPaths = null, int? Limit = null);

public sealed record ScanJobStatus(
    bool IsRunning,
    bool IsDryRun,
    bool IsPaused,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string FolderPath,
    IReadOnlyList<string> SelectedFolderPaths,
    string Message,
    PhotoAiScanProgress? LastProgress,
    PhotoAiRunSummaryDocument? Summary,
    string? RunLogPath,
    string? Error,
    IReadOnlyList<string> RecentLogLines)
{
    public static ScanJobStatus Idle() => new(
        IsRunning: false,
        IsDryRun: false,
        IsPaused: false,
        StartedAt: null,
        FinishedAt: null,
        FolderPath: string.Empty,
        SelectedFolderPaths: [],
        Message: "Idle.",
        LastProgress: null,
        Summary: null,
        RunLogPath: null,
        Error: null,
        RecentLogLines: []);
}

internal sealed record ImmichSyncConfig(string BaseUrl, string ApiKey);

internal sealed record ImmichSidecarQueueSnapshot(bool QueueIsActive, bool QueueIsPaused, int Active, int Waiting, int Delayed, int Paused)
{
    public bool IsBusy => QueueIsActive || QueueIsPaused || Active > 0 || Waiting > 0 || Delayed > 0 || Paused > 0;
}
