using CommunityToolkit.Mvvm.ComponentModel;
using PeydaBrowser.App.Services;

namespace PeydaBrowser.App.ViewModels;

/// <summary>Base class for native internal pages (peyda://...). The hosting tab
/// is attached by InternalPageFactory so pages can navigate their own tab.</summary>
public abstract class InternalPageViewModel(LocalizedStrings strings) : ObservableObject
{
    public LocalizedStrings Strings { get; } = strings;
    public BrowserTabViewModel? Tab { get; private set; }

    public void Attach(BrowserTabViewModel tab) => Tab = tab;

    protected void NavigateHere(string url) => Tab?.NavigateToUrl(url);
    protected void OpenInNewTab(string url) => Tab?.Shell.OpenNewTab(url, activate: true);

    public virtual Task LoadAsync(string? argument, CancellationToken ct = default) => Task.CompletedTask;
}
