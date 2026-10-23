namespace PeydaBrowser.Core.Services;

/// <summary>Smoothed download-speed estimation from byte-counter samples.</summary>
public sealed class DownloadSpeedCalculator
{
    private readonly Queue<(DateTimeOffset At, long Bytes)> _samples = new();
    private readonly TimeSpan _window;

    public DownloadSpeedCalculator(TimeSpan? window = null) => _window = window ?? TimeSpan.FromSeconds(5);

    /// <summary>Adds a sample and returns the average bytes/second over the window.</summary>
    public double AddSample(DateTimeOffset atUtc, long totalBytesReceived)
    {
        _samples.Enqueue((atUtc, totalBytesReceived));
        while (_samples.Count > 2 && atUtc - _samples.Peek().At > _window)
            _samples.Dequeue();
        if (_samples.Count < 2) return 0;
        var first = _samples.Peek();
        var seconds = (atUtc - first.At).TotalSeconds;
        if (seconds <= 0) return 0;
        var delta = totalBytesReceived - first.Bytes;
        return delta <= 0 ? 0 : delta / seconds;
    }

    public void Reset() => _samples.Clear();
}

public static class ByteSizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return "0 B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {Units[unit]}";
    }

    public static double ProgressPercent(long received, long total)
    {
        if (total <= 0) return 0;
        return Math.Clamp(received * 100d / total, 0, 100);
    }
}
