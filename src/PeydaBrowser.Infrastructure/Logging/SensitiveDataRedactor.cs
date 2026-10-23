using System.Text.RegularExpressions;

namespace PeydaBrowser.Infrastructure.Logging;

/// <summary>
/// Last line of defense against secrets in log files: values that look like
/// passwords, tokens, cookies or authorization headers are replaced before a
/// line is written. (Code review rules additionally forbid logging such data
/// at the call site — see docs/SECURITY.md.)
/// </summary>
public static partial class SensitiveDataRedactor
{
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|token|api[_-]?key|secret|session[_-]?id)\s*[:=]\s*([^\s,;]+)")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"(?i)\b(Authorization|Cookie|Set-Cookie)\s*:\s*([^\r\n]+)")]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerRegex();

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var redacted = KeyValueRegex().Replace(message, "$1=[redacted]");
        redacted = HeaderRegex().Replace(redacted, "$1: [redacted]");
        redacted = BearerRegex().Replace(redacted, "Bearer [redacted]");
        return redacted;
    }
}
