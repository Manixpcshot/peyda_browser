using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.Services;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Infrastructure.Files;
using PeydaBrowser.Infrastructure.Profiles;
using PeydaBrowser.Infrastructure.Updates;
using PeydaBrowser.Persistence;

namespace PeydaBrowser.App;

/// <summary>
/// Application entry point. Owns the profile registry and the set of open
/// windows. Every window runs against its own profile context; ephemeral
/// (private) contexts are deleted when their window closes.
/// </summary>
public partial class App : Application
{
    private sealed record WindowEntry(Window Window, ProfileContext Context, ServiceProvider Provider);

    private readonly List<WindowEntry> _windows = new();
    private AppPaths? _paths;
    private ProfileManager? _profileManager;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var startPrivate = commandLine.Any(a => string.Equals(a, "--private", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(a, "-private", StringComparison.OrdinalIgnoreCase));
        var initialUrl = commandLine.FirstOrDefault(a =>
            a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("peyda://", StringComparison.OrdinalIgnoreCase));

        _ = StartAsync(initialUrl, startPrivate);
    }

    private async Task StartAsync(string? initialUrl, bool startPrivate)
    {
        _paths = new AppPaths();
        _profileManager = new ProfileManager(_paths, new SystemClock());
        var defaultContext = await _profileManager.GetOrCreateDefaultContextAsync();
        await CreateWindowAsync(defaultContext, initialUrl);
        if (startPrivate)
            await CreatePrivateWindowAsync();
    }

    private async Task CreatePrivateWindowAsync()
    {
        if (_profileManager is null) return;
        var context = _profileManager.CreatePrivateContext();
        await CreateWindowAsync(context, initialUrl: null);
    }

    private async Task CreateWindowAsync(ProfileContext context, string? initialUrl)
    {
        var provider = AppServiceFactory.Build(context, _paths!);
        var logger = provider.GetRequiredService<ILogger<App>>();

        try
        {
            var database = provider.GetRequiredService<PeydaDatabase>();
            await database.InitializeAsync();
            await provider.GetRequiredService<DatabaseSeeder>().SeedAsync(context.Profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize profile database at {Path}", context.DatabasePath);
        }

        var viewModel = provider.GetRequiredService<MainViewModel>();
        await viewModel.LoadSettingsAsync();
        await viewModel.ThemeService.LoadAsync();

        var window = new MainWindow(viewModel, initialUrl);
        viewModel.OpenPrivateWindowAction = () => _ = CreatePrivateWindowAsync();

        var entry = new WindowEntry(window, context, provider);
        _windows.Add(entry);
        window.Closed += (_, _) => _ = CleanupWindowAsync(entry);

        window.Activate();
        _ = CheckWebView2RuntimeAsync(window, viewModel);

        // Optional, privacy-respecting update check: only queries GitHub for the
        // release list when the user left the toggle on; nothing downloads itself.
        if (!context.IsPrivate && viewModel.Settings.CheckForUpdatesAutomatically)
            _ = BackgroundUpdateCheckAsync(provider, viewModel, logger);
    }

    private static async Task BackgroundUpdateCheckAsync(IServiceProvider provider, MainViewModel viewModel, ILogger logger)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            var updateService = provider.GetRequiredService<GitHubUpdateService>();
            var channel = Core.Updates.UpdateChannelParser.Parse(viewModel.Settings.UpdateChannel);
            var result = await updateService.CheckForUpdatesAsync(
                GitHubUpdateService.GetCurrentVersion(typeof(App).Assembly), channel);
            if (result.AvailableUpdate is not null)
            {
                logger.LogInformation("A Peyda Browser update is available: {Tag}", result.AvailableUpdate.TagName);
                viewModel.OpenNewTab(result.AvailableUpdate.HtmlUrl, activate: false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Background update check failed");
        }
    }

    private static async Task CheckWebView2RuntimeAsync(Window window, MainViewModel viewModel)
    {
        string? version = null;
        try
        {
            version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            version = null;
        }
        if (version is not null) return;

        try
        {
            await Task.Delay(600);
            if (window.Content is not FrameworkElement { XamlRoot: { } root }) return;
            var strings = viewModel.Strings;
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = strings.AboutWebView2NotFound,
                Content = strings.AboutWebView2NotFound,
                PrimaryButtonText = strings.CommonOpen,
                CloseButtonText = strings.CommonClose,
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await Windows.System.Launcher.LaunchUriAsync(
                    new Uri("https://developer.microsoft.com/microsoft-edge/webview2/consumer/"));
            }
        }
        catch (Exception)
        {
            // The dialog is a convenience; failure must not kill the app.
        }
    }

    private async Task CleanupWindowAsync(WindowEntry entry)
    {
        _windows.Remove(entry);
        try
        {
            await entry.Provider.DisposeAsync();
        }
        catch (Exception)
        {
            // Disposal must never throw during shutdown.
        }

        SqliteConnection.ClearAllPools();

        if (entry.Context.Profile.IsEphemeral)
            _profileManager?.DisposeEphemeralContext(entry.Context);

        if (_windows.Count == 0)
            Exit();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var root = _paths?.RootDirectory ?? Path.GetTempPath();
            var logDir = Path.Combine(root, "Logs");
            Directory.CreateDirectory(logDir);
            var line = $"[{DateTimeOffset.UtcNow:O}] Unhandled: {e.Exception}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(logDir, "crash.log"), line);
        }
        catch (Exception)
        {
            // ignore - already in an error path
        }
        // Keep the app alive when possible; a tab-level failure should not
        // take down every window. Truly fatal states will still surface.
        e.Handled = true;
    }
}
