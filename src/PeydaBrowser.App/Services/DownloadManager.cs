using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Infrastructure.Files;

namespace PeydaBrowser.App.Services;

/// <summary>
/// Owns download state for one window/profile: live WebView2 operations,
/// persisted history rows and SHA-256 computation for completed files.
/// Downloads are collected here from every tab via BrowserTabView.
/// </summary>
public sealed class DownloadManager
{
    private readonly IDownloadRepository _repository;
    private readonly ILocalizationService _loc;
    private readonly ILogger<DownloadManager> _logger;

    public DownloadManager(IDownloadRepository repository, ILocalizationService loc, ILogger<DownloadManager> logger)
    {
        _repository = repository;
        _loc = loc;
        _logger = logger;
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public async Task LoadHistoryAsync(CancellationToken ct = default)
    {
        Items.Clear();
        foreach (var record in await _repository.GetAllAsync(ct).ConfigureAwait(true))
        {
            if (record.State is DownloadState.InProgress or DownloadState.Paused)
            {
                record.State = DownloadState.Interrupted;
                await _repository.UpsertAsync(record, ct).ConfigureAwait(true);
            }
            var vm = new DownloadItemViewModel(record, _loc);
            vm.SyncFromRecord();
            HookCommonActions(vm);
            Items.Add(vm);
        }
    }

    /// <summary>Registers a download that WebView2 has just started (UI thread).</summary>
    public async Task<DownloadItemViewModel> RegisterAsync(CoreWebView2DownloadOperation operation, DownloadRecord record)
    {
        var vm = new DownloadItemViewModel(record, _loc);
        vm.SyncFromRecord();
        vm.CanResumeFromEngine = operation.CanResume;
        vm.PauseAction = () => { try { operation.Pause(); } catch (Exception ex) { _logger.LogWarning(ex, "Pause failed for {File}", record.FileName); } };
        vm.ResumeAction = () => { try { operation.Resume(); } catch (Exception ex) { _logger.LogWarning(ex, "Resume failed for {File}", record.FileName); } };
        vm.CancelAction = () => { try { operation.Cancel(); } catch (Exception ex) { _logger.LogWarning(ex, "Cancel failed for {File}", record.FileName); } };
        HookCommonActions(vm);

        var lastPersist = DateTimeOffset.MinValue;
        operation.BytesReceivedChanged += (_, _) =>
        {
            vm.UpdateProgress(operation.BytesReceived, operation.TotalBytesToReceive, DateTimeOffset.UtcNow);
            vm.CanResumeFromEngine = operation.CanResume;
            var now = DateTimeOffset.UtcNow;
            if (now - lastPersist > TimeSpan.FromSeconds(2))
            {
                lastPersist = now;
                _ = PersistAsync(vm);
            }
        };
        operation.StateChanged += async (_, _) =>
        {
            vm.State = operation.State switch
            {
                CoreWebView2DownloadState.Completed => DownloadState.Completed,
                CoreWebView2DownloadState.Interrupted => MapInterrupted(operation),
                _ => vm.State == DownloadState.Paused && operation.CanResume ? DownloadState.Paused : DownloadState.InProgress
            };
            vm.CanResumeFromEngine = operation.CanResume;
            vm.UpdateProgress(operation.BytesReceived, operation.TotalBytesToReceive, DateTimeOffset.UtcNow);
            if (vm.State == DownloadState.Completed)
            {
                vm.Record.CompletedUtc = DateTimeOffset.UtcNow;
                await ComputeHashAsync(vm).ConfigureAwait(true);
            }
            await PersistAsync(vm).ConfigureAwait(true);
        };

        Items.Insert(0, vm);
        await PersistAsync(vm).ConfigureAwait(true);
        return vm;

        DownloadState MapInterrupted(CoreWebView2DownloadOperation op)
        {
            return op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled
                ? DownloadState.Cancelled
                : op.CanResume ? DownloadState.Paused : DownloadState.Interrupted;
        }
    }

    private void HookCommonActions(DownloadItemViewModel vm)
    {
        vm.RemoveRequested = async item =>
        {
            Items.Remove(item);
            await _repository.DeleteAsync(item.Record.Id).ConfigureAwait(true);
        };
        vm.OpenFileRequested = item => OpenFileAsync(item);
    }

    /// <summary>Set by the shell so dangerous files get a confirmation dialog first.</summary>
    public Func<DownloadItemViewModel, Task<bool>>? ConfirmOpenDangerousAsync { get; set; }

    private async Task OpenFileAsync(DownloadItemViewModel item)
    {
        if (!File.Exists(item.Record.TargetPath)) return;
        if (item.IsDangerous && ConfirmOpenDangerousAsync is not null)
        {
            var confirmed = await ConfirmOpenDangerousAsync(item).ConfigureAwait(true);
            if (!confirmed) return;
        }
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Record.TargetPath);
        await Windows.System.Launcher.LaunchFileAsync(file);
    }

    public async Task ClearCompletedAsync(CancellationToken ct = default)
    {
        await _repository.ClearCompletedAsync(ct).ConfigureAwait(true);
        var removable = Items.Where(i => !i.IsLive).ToList();
        foreach (var item in removable) Items.Remove(item);
    }

    private async Task PersistAsync(DownloadItemViewModel vm)
    {
        try
        {
            vm.ApplyToRecord();
            await _repository.UpsertAsync(vm.Record).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist download {File}", vm.Record.FileName);
        }
    }

    private async Task ComputeHashAsync(DownloadItemViewModel vm)
    {
        try
        {
            if (File.Exists(vm.Record.TargetPath))
                vm.Record.Sha256 = await FileIntegrity.ComputeSha256Async(vm.Record.TargetPath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hash downloaded file {File}", vm.Record.FileName);
        }
    }
}
