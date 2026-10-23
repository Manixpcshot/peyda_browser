using Microsoft.Extensions.DependencyInjection;
using PeydaBrowser.Infrastructure.Files;
using PeydaBrowser.Infrastructure.Profiles;
using PeydaBrowser.Infrastructure.Updates;

namespace PeydaBrowser.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPeydaInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<ProfileManager>();
        services.AddSingleton<ThemeFileService>();
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        services.AddSingleton<GitHubUpdateService>();
        return services;
    }
}
