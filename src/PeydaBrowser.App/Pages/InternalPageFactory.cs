using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.WebEngine;

namespace PeydaBrowser.App.Pages;

/// <summary>Creates the native control (with a fresh view model) for an internal
/// page inside a specific tab. Internal pages are never HTML/WebView2 content.</summary>
public static class InternalPageFactory
{
    public static UIElement Create(InternalPage page, BrowserTabViewModel tab)
    {
        var services = ((MainViewModel)tab.Shell).Services;
        switch (page)
        {
            case InternalPage.Settings:
                return Wrap<SettingsViewModel, SettingsPage>(services, tab);
            case InternalPage.History:
                return Wrap<HistoryViewModel, HistoryPage>(services, tab);
            case InternalPage.Downloads:
                return Wrap<DownloadsViewModel, DownloadsPage>(services, tab);
            case InternalPage.Bookmarks:
                return Wrap<BookmarksViewModel, BookmarksPage>(services, tab);
            case InternalPage.About:
                return Wrap<AboutViewModel, AboutPage>(services, tab);
            case InternalPage.PrivateHome:
                return Wrap<PrivateHomeViewModel, PrivateHomePage>(services, tab);
            case InternalPage.Error:
            case InternalPage.Unknown:
                return new InternalErrorPage { DataContext = tab.Shell.Strings };
            case InternalPage.NewTab:
            default:
                return Wrap<NewTabViewModel, NewTabPage>(services, tab);
        }
    }

    private static UIElement Wrap<TViewModel, TControl>(IServiceProvider services, BrowserTabViewModel tab)
        where TViewModel : InternalPageViewModel
        where TControl : UserControl, new()
    {
        var vm = services.GetRequiredService<TViewModel>();
        vm.Attach(tab);
        var control = new TControl { DataContext = vm };
        control.Loaded += async (_, _) =>
        {
            try { await vm.LoadAsync(tab.InternalPageArgument); }
            catch (Exception) { /* page load failures are logged by services; keep the page usable */ }
        };
        return control;
    }
}
