using Microsoft.Extensions.DependencyInjection;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Persistence.Repositories;

namespace PeydaBrowser.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the profile database and all repositories as singletons.
    /// Call once per browser profile (per window context).</summary>
    public static IServiceCollection AddPeydaPersistence(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new PeydaDatabaseOptions { DatabasePath = databasePath });
        services.AddSingleton<PeydaDatabase>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<ISearchEngineRepository, SqliteSearchEngineRepository>();
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();
        services.AddSingleton<IBookmarkRepository, SqliteBookmarkRepository>();
        services.AddSingleton<IDownloadRepository, SqliteDownloadRepository>();
        services.AddSingleton<ISessionRepository, SqliteSessionRepository>();
        services.AddSingleton<IProfileRepository, SqliteProfileRepository>();
        services.AddSingleton<IThemeRepository, SqliteThemeRepository>();
        services.AddSingleton<IShortcutRepository, SqliteShortcutRepository>();
        services.AddSingleton<IPermissionRepository, SqlitePermissionRepository>();
        services.AddSingleton<DatabaseSeeder>();
        return services;
    }
}
