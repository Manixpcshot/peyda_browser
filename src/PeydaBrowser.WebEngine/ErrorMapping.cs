namespace PeydaBrowser.WebEngine;

public enum PageErrorKind
{
    DnsFailure,
    ConnectionFailed,
    Timeout,
    CertificateError,
    HttpError,
    Unknown
}

/// <summary>Maps WebView2 <c>CoreWebView2WebErrorStatus</c> names (passed as strings)
/// to a small set of user-facing error categories for the native error page.</summary>
public static class BrowserErrorMapper
{
    public static PageErrorKind FromWebErrorStatusName(string? statusName) => statusName switch
    {
        "CannotConnect" => PageErrorKind.ConnectionFailed,
        "ConnectionAborted" => PageErrorKind.ConnectionFailed,
        "ConnectionReset" => PageErrorKind.ConnectionFailed,
        "Disconnected" => PageErrorKind.ConnectionFailed,
        "HostNameNotResolved" => PageErrorKind.DnsFailure,
        "Timeout" => PageErrorKind.Timeout,
        "OperationCanceled" => PageErrorKind.Unknown,
        "CertificateCommonNameIsIncorrect" => PageErrorKind.CertificateError,
        "CertificateExpired" => PageErrorKind.CertificateError,
        "CertificateAuthorityInvalid" => PageErrorKind.CertificateError,
        "CertificateRevoked" => PageErrorKind.CertificateError,
        "CertificateIsInvalid" => PageErrorKind.CertificateError,
        "ServerUnreachable" => PageErrorKind.ConnectionFailed,
        "RedirectFailed" => PageErrorKind.HttpError,
        "UnexpectedError" => PageErrorKind.Unknown,
        _ => PageErrorKind.Unknown
    };
}

/// <summary>Engine configuration values the App layer applies to WebView2 settings.</summary>
public sealed class WebEngineOptions
{
    public bool DevToolsEnabled { get; init; }
    public bool DefaultContextMenusEnabled { get; init; } = true;
    public bool StatusBarEnabled { get; init; }
    public bool BrowserAcceleratorKeysEnabled { get; init; }
    public bool PasswordAutosaveEnabled { get; init; }
    public bool GeneralAutofillEnabled { get; init; }
}
