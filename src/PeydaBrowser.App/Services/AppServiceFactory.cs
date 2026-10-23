using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Infrastructure;
using PeydaBrowser.Infrastructure.Files;
using PeydaBrowser.Infrastructure.Logging;
using PeydaBrowser.Infrastructure.Profiles;
using PeydaBrowser.Persistence;

namespace PeydaBrowser.App.Services;

/// <summary>
/// Composition root for one browser window/profile. Each window gets its own
/// <see cref="ServiceProvider"/> bound to that profile's database, WebView2
/// user-data folder and log directory, so profiles (and private windows) are
/// fully isolated from each other.
/// </summary>
public static class AppServiceFactory
{
    public static ServiceProvider Build(ProfileContext context, AppPaths paths)
    {
        var services = new ServiceCollection();

        services.AddSingleton(context);
        services.AddPeydaInfrastructure(paths);
        services.AddPeydaPersistence(context.DatabasePath);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<LocalizedStrings>();

        services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(context.LogsDirectory, LogLevel.Information));
        }));
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Core browser services
        services.AddSingleton<AddressBarParser>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<SearchEngineService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<OmniboxSuggestionService>();

        // Shell
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IBrowserShell>(sp => sp.GetRequiredService<MainViewModel>());

        // Internal page view models (fresh instance per navigation)
        services.AddTransient<NewTabViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<BookmarksViewModel>();
        services.AddTransient<DownloadsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<PrivateHomeViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
