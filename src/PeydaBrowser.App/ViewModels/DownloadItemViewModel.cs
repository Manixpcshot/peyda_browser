using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.App.ViewModels;

/// <summary>One row in the Downloads UI. Live downloads are wired to WebView2
/// operations through the action delegates; historical rows come from the database.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;
    private readonly DownloadSpeedCalculator _speed = new();

    public DownloadItemViewModel(DownloadRecord record, ILocalizationService loc)
    {
        Record = record;
        _loc = loc;
    }

    public DownloadRecord Record { get; }

    public Action? PauseAction { get; set; }
    public Action? ResumeAction { get; set; }
    public Action? CancelAction { get; set; }
    public Func<DownloadItemViewModel, Task>? RemoveRequested { get; set; }
    public Func<DownloadItemViewModel, Task>? OpenFileRequested { get; set; }

    public string FileName => Record.FileName;
    public string SourceUrl => Record.SourceUrl;
    public string TargetPath => Record.TargetPath;
    public bool IsDangerous => DangerousFileClassifier.IsPotentiallyDangerous(Record.FileName) ||
                               DangerousFileClassifier.HasDeceptiveDoubleExtension(Record.FileName);
    public bool FileExists => System.IO.File.Exists(Record.TargetPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(ProgressPercent), nameof(IsLive), nameof(CanPause), nameof(CanResume), nameof(CanCancel), nameof(SizeText))]
    public partial DownloadState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent), nameof(SizeText))]
    public partial long ReceivedBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent), nameof(SizeText), nameof(HasDeterminateProgress))]
    public partial long TotalBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
    public partial double BytesPerSecond { get; set; }

    [ObservableProperty]
    public partial bool CanResumeFromEngine { get; set; }

    public bool IsLive => State is DownloadState.InProgress or DownloadState.Paused;
    public bool CanPause => State == DownloadState.InProgress && PauseAction is not null;
    public bool CanResume => (State == DownloadState.Paused || (State == DownloadState.Interrupted && CanResumeFromEngine)) && ResumeAction is not null;
    public bool CanCancel => IsLive && CancelAction is not null;
    public bool HasDeterminateProgress => TotalBytes > 0;
    public double ProgressPercent => ByteSizeFormatter.ProgressPercent(ReceivedBytes, TotalBytes);

    public string StateText => _loc[State switch
    {
        DownloadState.InProgress => LocKeys.DownloadsStateInProgress,
        DownloadState.Paused => LocKeys.DownloadsStatePaused,
        DownloadState.Completed => LocKeys.DownloadsStateCompleted,
        DownloadState.Cancelled => LocKeys.DownloadsStateCancelled,
        DownloadState.Failed => LocKeys.DownloadsStateFailed,
        _ => LocKeys.DownloadsStateInterrupted
    }];

    public string SizeText => TotalBytes > 0
        ? $"{ByteSizeFormatter.Format(ReceivedBytes)} / {ByteSizeFormatter.Format(TotalBytes)}"
        : ByteSizeFormatter.Format(ReceivedBytes);

    public string SpeedText => State == DownloadState.InProgress && BytesPerSecond > 0
        ? $"{ByteSizeFormatter.Format((long)BytesPerSecond)}/s"
        : string.Empty;

    public void SyncFromRecord()
    {
        State = Record.State;
        ReceivedBytes = Record.ReceivedBytes;
        TotalBytes = Record.TotalBytes;
    }

    public void UpdateProgress(long received, long total, DateTimeOffset nowUtc)
    {
        ReceivedBytes = received;
        if (total > 0) TotalBytes = total;
        BytesPerSecond = _speed.AddSample(nowUtc, received);
    }

    public void ApplyToRecord()
    {
        Record.State = State;
        Record.ReceivedBytes = ReceivedBytes;
        Record.TotalBytes = TotalBytes;
    }

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(SizeText));
    }

    [RelayCommand]
    private void Pause() => PauseAction?.Invoke();

    [RelayCommand]
    private void Resume() => ResumeAction?.Invoke();

    [RelayCommand]
    private void Cancel() => CancelAction?.Invoke();

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (RemoveRequested is not null) await RemoveRequested(this);
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (OpenFileRequested is not null) await OpenFileRequested(this);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = Path.GetDirectoryName(Record.TargetPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var storageFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folder);
        await Windows.System.Launcher.LaunchFolderAsync(storageFolder);
    }
}
