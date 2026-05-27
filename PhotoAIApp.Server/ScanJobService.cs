using PhotoAIApp.Core;
using System.Collections.Concurrent;

namespace ImmichTagger.Server;

public sealed class ScanJobService
{
    private readonly PhotoAiScanner _scanner = new();
    private readonly object _gate = new();
    private readonly ConcurrentQueue<string> _recentLogLines = new();
    private CancellationTokenSource? _activeCancellation;
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
            _recentLogLines.Clear();
            _status = new ScanJobStatus(
                IsRunning: true,
                IsDryRun: dryRun,
                StartedAt: DateTimeOffset.Now,
                FinishedAt: null,
                FolderPath: request.FolderPath,
                Message: dryRun ? "Dry run started." : "Live scan started.",
                LastProgress: null,
                Summary: null,
                Error: null,
                RecentLogLines: []);

            PhotoAiScanOptions options = settings.ToScanOptions(request.FolderPath, dryRun, request.Limit);
            CancellationToken token = _activeCancellation.Token;
            _activeTask = Task.Run(() => RunScanAsync(options, token), CancellationToken.None);
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

    private async Task RunScanAsync(PhotoAiScanOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var progress = new Progress<PhotoAiScanProgress>(OnProgress);
            PhotoAiScanSummary summary = await _scanner.ScanFolderAsync(options, progress, cancellationToken);
            lock (_gate)
            {
                _status = _status with
                {
                    IsRunning = false,
                    FinishedAt = DateTimeOffset.Now,
                    Message = options.DryRun ? "Dry run complete." : "Live scan complete.",
                    Summary = PhotoAiRunSummaryDocument.FromSummary(summary),
                    Error = null
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
                    FinishedAt = DateTimeOffset.Now,
                    Message = "Scan cancelled.",
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
                    FinishedAt = DateTimeOffset.Now,
                    Message = "Scan failed.",
                    Error = ex.Message
                };
            }
        }
    }

    private void OnProgress(PhotoAiScanProgress progress)
    {
        _recentLogLines.Enqueue(progress.Message);
        while (_recentLogLines.Count > 25 && _recentLogLines.TryDequeue(out _))
        {
        }

        lock (_gate)
        {
            _status = _status with
            {
                Message = progress.Message,
                LastProgress = progress
            };
        }
    }
}

public sealed record ScanStartRequest(string FolderPath, int? Limit = null);

public sealed record ScanJobStatus(
    bool IsRunning,
    bool IsDryRun,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string FolderPath,
    string Message,
    PhotoAiScanProgress? LastProgress,
    PhotoAiRunSummaryDocument? Summary,
    string? Error,
    IReadOnlyList<string> RecentLogLines)
{
    public static ScanJobStatus Idle() => new(
        IsRunning: false,
        IsDryRun: true,
        StartedAt: null,
        FinishedAt: null,
        FolderPath: string.Empty,
        Message: "Idle.",
        LastProgress: null,
        Summary: null,
        Error: null,
        RecentLogLines: []);
}
