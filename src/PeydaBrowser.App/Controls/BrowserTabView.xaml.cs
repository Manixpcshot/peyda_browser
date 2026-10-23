using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using PeydaBrowser.App.Pages;
using PeydaBrowser.App.Services;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;
using PeydaBrowser.WebEngine;
using LocKeys = PeydaBrowser.Core.Services.LocKeys;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace PeydaBrowser.App.Controls;

/// <summary>
/// Hosts one tab's Microsoft Edge WebView2 control (Chromium engine) plus the
/// native internal-page host and the native error/crash overlay. All engine
/// security gates (navigation policy, permissions, certificates, downloads,
/// external protocols) are enforced here, delegating decisions to
/// PeydaBrowser.WebEngine / Core policies.
/// </summary>
public sealed partial class BrowserTabView : UserControl, IBrowserTabView
{
    private BrowserTabViewModel? _vm;
    private WebView2? _webView;
    private bool _initialized;
    private bool _initializing;
    private bool _browserProcessExited;
    private string? _pendingUrl;
    private bool _pendingMuted;

    public BrowserTabView()
    {
        InitializeComponent();
    }

    private IBrowserShell Shell => _vm!.Shell;
    private CoreWebView2? Core => _webView?.CoreWebView2;

    public void Attach(BrowserTabViewModel vm)
    {
        _vm = vm;
        vm.View = this;
        DataContext = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateHosts();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserTabViewModel.CurrentInternalPage):
                if (_vm!.CurrentInternalPage is { } page)
                    InternalHost.Content = InternalPageFactory.Create(page, _vm);
                else
                    InternalHost.Content = null;
                UpdateHosts();
                break;
            case nameof(BrowserTabViewModel.ErrorKind):
            case nameof(BrowserTabViewModel.IsCrashed):
                UpdateErrorOverlay();
                UpdateHosts();
                break;
            case nameof(BrowserTabViewModel.IsLoading):
                LoadingBar.Visibility = _vm!.IsLoading && _vm.CurrentInternalPage is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                break;
        }
    }

    private void UpdateHosts()
    {
        if (_vm is null) return;
        var internalVisible = _vm.CurrentInternalPage is not null;
        var errorVisible = _vm.ShowErrorPage;
        InternalHost.Visibility = internalVisible ? Visibility.Visible : Visibility.Collapsed;
        ErrorHost.Visibility = errorVisible ? Visibility.Visible : Visibility.Collapsed;
        WebHost.Visibility = !internalVisible && !errorVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateErrorOverlay()
    {
        if (_vm is null) return;
        var strings = Shell.Strings;
        ErrorTitle.Text = _vm.IsCrashed ? strings.TabCrashedTitle : strings.ErrorTitle;
        ErrorMessage.Text = _vm.IsCrashed ? strings.TabCrashedMessage : strings.ErrorMessage;
        ErrorReloadButton.Content = strings.NavReload;
    }

    private void OnErrorReloadClick(object sender, RoutedEventArgs e) => Reload(ignoreCache: false);

    // ============================================================ IBrowserTabView

    public async Task EnsureInitializedAsync()
    {
        if (_initialized || _initializing || _vm is null) return;
        _initializing = true;
        try
        {
            _webView ??= CreateWebViewControl();
            var environment = await Shell.GetWebViewEnvironmentAsync();
            await _webView.EnsureCoreWebView2Async(environment);
            ConfigureCore();
            _initialized = true;
            _browserProcessExited = false;

            if (_pendingMuted) Core!.IsMuted = true;
            if (_pendingUrl is not null)
            {
                var url = _pendingUrl;
                _pendingUrl = null;
                Core!.Navigate(url);
            }
        }
        catch (Exception)
        {
            // Missing WebView2 Runtime or a failed browser process start. The
            // tab shows the native crash page; the rest of the app keeps working.
            _initialized = false;
            _vm?.ShowCrashed();
        }
        finally
        {
            _initializing = false;
        }
    }

    private WebView2 CreateWebViewControl()
    {
        var control = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        control.KeyDown += OnWebViewKeyDown;
        WebHost.Children.Add(control);
        return control;
    }

    private void ConfigureCore()
    {
        var core = Core!;
        var settings = Shell.Settings;
        var engine = new WebEngineOptions { DevToolsEnabled = settings.DevToolsEnabled };

        core.Settings.AreDevToolsEnabled = engine.DevToolsEnabled;
        core.Settings.AreBrowserAcceleratorKeysEnabled = engine.BrowserAcceleratorKeysEnabled;
        core.Settings.AreDefaultContextMenusEnabled = engine.DefaultContextMenusEnabled;
        core.Settings.IsStatusBarEnabled = engine.StatusBarEnabled;
        core.Settings.AreHostObjectsAllowed = false;      // no COM host objects for web content
        core.Settings.IsWebMessageEnabled = false;        // Peyda injects no scripts and reads no page messages
        core.Settings.IsScriptEnabled = true;
        core.Settings.IsPasswordAutosaveEnabled = engine.PasswordAutosaveEnabled;
        core.Settings.IsGeneralAutofillEnabled = true;
        core.Settings.IsZoomControlEnabled = false;       // Peyda provides its own zoom commands

        ApplyPreferredColorScheme(((MainViewModel)Shell).ThemeService.IsDarkEffective);

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.SourceChanged += OnSourceChanged;
        core.HistoryChanged += OnHistoryChanged;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.FaviconChanged += OnFaviconChanged;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.ServerCertificateErrorDetected += OnServerCertificateError;
        core.ProcessFailed += OnProcessFailed;
        core.DownloadStarting += OnDownloadStarting;
        core.ContainsFullScreenElementChanged += OnFullScreenChanged;
    }

    public void NavigateTo(string url)
    {
        var decision = NavigationPolicy.Decide(url, isMainFrame: true, isUserTyped: true);
        switch (decision.Action)
        {
            case NavigationAction.OpenInternalPage when decision.InternalRoute is not null:
                _vm?.ShowInternalPage(decision.InternalRoute);
                return;
            case NavigationAction.Block:
                return;
            case NavigationAction.AskExternalApp:
                _ = ConfirmAndLaunchExternalAsync(url);
                return;
        }

        if (!_initialized)
        {
            _pendingUrl = url;
            _ = EnsureInitializedAsync();
            return;
        }
        Core?.Navigate(url);
    }

    public void GoBack()
    {
        if (_vm is null) return;
        if (_vm.ReturnToWebPage()) return;
        if (Core?.CanGoBack == true) Core.GoBack();
    }

    public void GoForward()
    {
        if (Core?.CanGoForward == true) Core.GoForward();
    }

    public void Reload(bool ignoreCache)
    {
        if (_vm is null) return;
        if (!_initialized || _browserProcessExited)
        {
            _ = RecreateAndNavigateAsync();
            return;
        }
        _vm.ShowWebPage();
        if (!ignoreCache)
        {
            Core?.Reload();
            return;
        }
        _ = HardReloadAsync();
    }

    private async Task HardReloadAsync()
    {
        try
        {
            await Core!.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
            await Core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}");
        }
        catch (Exception)
        {
            try { Core?.Reload(); } catch (Exception) { /* control may be gone */ }
        }
    }

    private async Task RecreateAndNavigateAsync()
    {
        try
        {
            if (_webView is not null)
            {
                try { _webView.Close(); } catch (Exception) { /* already gone */ }
                WebHost.Children.Remove(_webView);
                _webView = null;
            }
            _initialized = false;
            _browserProcessExited = false;
            _pendingUrl = string.IsNullOrEmpty(_vm!.LastWebUrl) ? _vm.CurrentUrl : _vm.LastWebUrl;
            _vm.ShowWebPage();
            await EnsureInitializedAsync();
        }
        catch (Exception)
        {
            _vm?.ShowCrashed();
        }
    }

    public void Stop()
    {
        try { Core?.Stop(); } catch (Exception) { /* ignored: stopping a gone control */ }
    }

    public void SetMuted(bool muted)
    {
        _pendingMuted = muted;
        if (_initialized && Core is not null)
        {
            try { Core.IsMuted = muted; } catch (Exception) { /* renderer may be gone */ }
        }
    }

    private double _zoomFactor = 1.0;

    /// <summary>
    /// The WinUI WebView2 control (unlike the WPF one) exposes no ZoomFactor,
    /// and the XAML wrapper hides the underlying controller. Peyda therefore
    /// applies Chromium's CSS zoom on the document element and re-applies it
    /// after every completed navigation.
    /// </summary>
    public double ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            _zoomFactor = Math.Clamp(value, 0.25, 5.0);
            _ = ApplyZoomAsync();
        }
    }

    private async Task ApplyZoomAsync()
    {
        if (Core is null) return;
        try
        {
            var factor = _zoomFactor.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Core.ExecuteScriptAsync($"document.documentElement.style.zoom = '{factor}';");
        }
        catch (Exception) { /* renderer busy/gone; zoom re-applies on next navigation */ }
    }

    public void OpenDevTools()
    {
        if (!Shell.Settings.DevToolsEnabled) return;
        try { Core?.OpenDevToolsWindow(); } catch (Exception) { /* ignore */ }
    }

    public void Print()
    {
        try { Core?.ShowPrintUI(CoreWebView2PrintDialogKind.System); } catch (Exception) { /* printing unavailable */ }
    }

    public async Task ClearSiteDataAsync(ClearTimeRange range)
    {
        if (!_initialized || Core is null) return;
        var kinds = CoreWebView2BrowsingDataKinds.DiskCache
                    | CoreWebView2BrowsingDataKinds.Cookies
                    | CoreWebView2BrowsingDataKinds.AllDomStorage
                    | CoreWebView2BrowsingDataKinds.CacheStorage;
        var cutoff = range.GetCutoffUtc(DateTimeOffset.UtcNow);
        if (cutoff is null)
            await Core.Profile.ClearBrowsingDataAsync(kinds);
        else
            await Core.Profile.ClearBrowsingDataAsync(kinds, cutoff.Value.UtcDateTime, DateTime.UtcNow);
    }

    public void ApplyPreferredColorScheme(bool isDark)
    {
        if (Core is null) return;
        try
        {
            Core.Profile.PreferredColorScheme = isDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
        }
        catch (Exception) { /* profile may be gone after crash */ }
    }

    public void CloseWebView()
    {
        try { _webView?.Close(); } catch (Exception) { /* already gone */ }
        _webView = null;
        _initialized = false;
    }

    public Task FocusWebContentAsync()
    {
        if (_webView is not null && _vm?.CurrentInternalPage is null)
            _webView.Focus(FocusState.Programmatic);
        return Task.CompletedTask;
    }

    // ============================================================ keyboard

    private async void OnWebViewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_vm is null) return;
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (KeyboardShortcutRouter.TryCreateGesture(e.Key, ctrl, alt, shift, out var gesture))
        {
            e.Handled = true;
            await KeyboardShortcutRouter.TryExecuteAsync(gesture, (MainViewModel)Shell);
        }
    }

    // ============================================================ engine events

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_vm is null) return;
        var decision = NavigationPolicy.Decide(e.Uri, isMainFrame: true, isUserTyped: e.IsUserInitiated);
        switch (decision.Action)
        {
            case NavigationAction.OpenInternalPage when decision.InternalRoute is not null:
                e.Cancel = true;
                _vm.ShowInternalPage(decision.InternalRoute);
                return;
            case NavigationAction.Block:
                e.Cancel = true;
                return;
            case NavigationAction.AskExternalApp:
                e.Cancel = true;
                _ = ConfirmAndLaunchExternalAsync(e.Uri);
                return;
        }
        _vm.ShowWebPage();
        _vm.OnNavigationStarted(e.Uri);
    }

    private async Task ConfirmAndLaunchExternalAsync(string url)
    {
        if (XamlRoot is null) return;
        var confirmed = await Shell.ConfirmExternalProtocolAsync(url, XamlRoot);
        if (confirmed && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Launcher.LaunchUriAsync(uri);
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_zoomFactor != 1.0) _ = ApplyZoomAsync();
        if (_vm is null) return;
        _vm.OnHistoryStateChanged(sender.CanGoBack, sender.CanGoForward);
        var kind = e.IsSuccess ? (PageErrorKind?)null : BrowserErrorMapper.FromWebErrorStatusName(e.WebErrorStatus.ToString());
        _ = _vm.OnNavigationCompletedAsync(e.IsSuccess, kind, sender.Source, sender.DocumentTitle, sender.FaviconUri);
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs e) =>
        _vm?.OnSourceChanged(sender.Source);

    private void OnHistoryChanged(CoreWebView2 sender, object e) =>
        _vm?.OnHistoryStateChanged(sender.CanGoBack, sender.CanGoForward);

    private void OnDocumentTitleChanged(CoreWebView2 sender, object e) =>
        _vm?.OnTitleChangedFromEngine(sender.DocumentTitle);

    private async void OnFaviconChanged(CoreWebView2 sender, object e)
    {
        if (_vm is null) return;
        try
        {
            using var stream = await sender.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            _vm.Favicon = bitmap;
        }
        catch (Exception)
        {
            // Favicons are best-effort decoration.
        }
    }

    private async void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_vm is null) return;
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            var settings = Shell.Settings;
            if (!PermissionPolicy.ShouldAllowPopup(e.IsUserInitiated, settings.BlockPopups))
                return;

            var decision = NavigationPolicy.Decide(e.Uri, isMainFrame: true, isUserTyped: e.IsUserInitiated);
            switch (decision.Action)
            {
                case NavigationAction.Allow:
                    if (settings.OpenNewWindowRequestsInTab)
                        Shell.OpenNewTab(e.Uri, activate: e.IsUserInitiated);
                    else
                        _vm.NavigateToUrl(e.Uri);
                    break;
                case NavigationAction.OpenInternalPage when decision.InternalRoute is not null:
                    Shell.OpenNewTab(InternalPageRouter.ToUrl(decision.InternalRoute.Page), activate: true);
                    break;
                case NavigationAction.AskExternalApp:
                    await ConfirmAndLaunchExternalAsync(e.Uri);
                    break;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (_vm is null) return;
        var deferral = e.GetDeferral();
        try
        {
            // Peyda keeps its own permission store (per profile DB) instead of
            // silently inheriting WebView2 profile-level decisions.
            e.SavesInProfile = false;
            var origin = UrlValidator.GetOrigin(e.Uri);
            var kind = PermissionPolicy.MapKind(e.PermissionKind.ToString());
            if (origin is null)
            {
                e.State = CoreWebView2PermissionState.Deny;
                return;
            }

            var services = ((MainViewModel)Shell).Services;
            var permissions = services.GetService(typeof(IPermissionRepository)) as IPermissionRepository;
            var stored = permissions is null ? null : await permissions.GetAsync(origin, kind);
            if (stored?.Decision is PermissionDecision.Allow or PermissionDecision.Deny)
            {
                e.State = stored.Decision == PermissionDecision.Allow
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;
                return;
            }

            var answer = await AskPermissionAsync(origin, kind);
            if (answer is null)
            {
                e.State = CoreWebView2PermissionState.Deny;
                return;
            }

            var (allow, remember) = answer.Value;
            e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            if (remember && permissions is not null && !Shell.IsPrivateWindow)
            {
                await permissions.UpsertAsync(new SitePermission
                {
                    Origin = origin,
                    Kind = kind,
                    Decision = allow ? PermissionDecision.Allow : PermissionDecision.Deny,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
            }
        }
        catch (Exception)
        {
            e.State = CoreWebView2PermissionState.Deny;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<(bool Allow, bool Remember)?> AskPermissionAsync(string origin, BrowserPermissionKind kind)
    {
        if (XamlRoot is null) return null;
        var strings = Shell.Strings;
        var kindText = strings.Inner[kind switch
        {
            BrowserPermissionKind.Camera => LocKeys.PermissionCamera,
            BrowserPermissionKind.Microphone => LocKeys.PermissionMicrophone,
            BrowserPermissionKind.Geolocation => LocKeys.PermissionGeolocation,
            BrowserPermissionKind.Notifications => LocKeys.PermissionNotifications,
            BrowserPermissionKind.ClipboardRead => LocKeys.PermissionClipboard,
            _ => LocKeys.PermissionOther
        }];
        var remember = new CheckBox { Content = strings.PermissionRemember };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = strings.Format(LocKeys.PermissionMessage, origin, kindText),
            TextWrapping = TextWrapping.WrapWholeWords
        });
        content.Children.Add(remember);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = strings.PermissionTitle,
            Content = content,
            PrimaryButtonText = strings.PermissionAllow,
            CloseButtonText = strings.PermissionDeny,
            DefaultButton = ContentDialogButton.Close
        };
        var result = await dialog.ShowAsync();
        return (result == ContentDialogResult.Primary, remember.IsChecked == true);
    }

    private async void OnServerCertificateError(CoreWebView2 sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        // Secure default: deny unless the user explicitly proceeds.
        var deferral = e.GetDeferral();
        try
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            if (XamlRoot is null || _vm is null) return;
            var strings = Shell.Strings;
            var host = Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var uri) ? uri.Host : e.RequestUri;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = strings.ErrorCertTitle,
                Content = strings.Format(LocKeys.ErrorCertMessage, host, e.ErrorStatus.ToString()),
                PrimaryButtonText = strings.ErrorCertProceed,
                CloseButtonText = strings.ErrorCertGoBack,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                _browserProcessExited = true;
                _initialized = false;
                Shell.InvalidateWebViewEnvironment();
                _vm?.ShowCrashed();
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                _vm?.ShowCrashed();
                break;
            // Sub-frame and helper process failures do not take down the tab.
        }
    }

    private async void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (_vm is null) return;
        var deferral = e.GetDeferral();
        try
        {
            var operation = e.DownloadOperation;
            var suggestedName = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = "download";

            var settings = Shell.Settings;
            string targetPath;
            if (settings.AskWhereToSaveDownloads)
            {
                var picked = await Shell.PickDownloadPathAsync(suggestedName);
                if (picked is null)
                {
                    e.Cancel = true;
                    return;
                }
                targetPath = picked;
            }
            else
            {
                var folder = !string.IsNullOrWhiteSpace(settings.DownloadFolder) && Directory.Exists(settings.DownloadFolder)
                    ? settings.DownloadFolder
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(folder);
                targetPath = MakeUniquePath(Path.Combine(folder, suggestedName));
            }

            e.ResultFilePath = targetPath;
            e.Handled = true; // Peyda renders its own download UI; nothing auto-executes.

            var record = new DownloadRecord
            {
                Id = "dl-" + Guid.NewGuid().ToString("N")[..12],
                FileName = Path.GetFileName(targetPath),
                SourceUrl = operation.Uri ?? string.Empty,
                TargetPath = targetPath,
                TotalBytes = Math.Max(0, operation.TotalBytesToReceive),
                ReceivedBytes = operation.BytesReceived,
                State = DownloadState.InProgress,
                StartedUtc = DateTimeOffset.UtcNow
            };
            await Shell.Downloads.RegisterAsync(operation, record);
        }
        catch (Exception)
        {
            e.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    private void OnFullScreenChanged(CoreWebView2 sender, object e) =>
        Shell.SetFullscreen(sender.ContainsFullScreenElement);
}
