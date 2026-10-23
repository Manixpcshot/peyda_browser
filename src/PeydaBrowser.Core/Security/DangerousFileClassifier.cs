namespace PeydaBrowser.Core.Security;

/// <summary>
/// Classifies downloaded file names so the UI can warn before the user opens
/// something executable. Downloads are never executed automatically.
/// </summary>
public static class DangerousFileClassifier
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".com", ".scr", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".jse", ".wsf", ".wsh", ".reg", ".jar", ".dll", ".sys", ".cpl", ".hta", ".lnk", ".pif",
        ".gadget", ".application", ".xaml", ".xbap", ".msc", ".sct", ".shb", ".inf", ".rgs"
    };

    public static bool IsPotentiallyDangerous(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && ExecutableExtensions.Contains(ext);
    }

    /// <summary>Detects double extensions such as "invoice.pdf.exe".</summary>
    public static bool HasDeceptiveDoubleExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsPotentiallyDangerous(fileName)) return false;
        var withoutLast = Path.GetFileNameWithoutExtension(fileName);
        var inner = Path.GetExtension(withoutLast);
        return !string.IsNullOrEmpty(inner) && !ExecutableExtensions.Contains(inner);
    }
}
