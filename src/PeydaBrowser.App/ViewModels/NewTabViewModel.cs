using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PeydaBrowser.App.Services;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Security;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.App.ViewModels;

public sealed partial class NewTabViewModel : InternalPageViewModel
{
    private readonly IShortcutRepository _shortcuts;
    private readonly SearchEngineService _searchEngines;
    private readonly ILocalizationService _loc;

    public NewTabViewModel(
        LocalizedStrings strings,
        IShortcutRepository shortcuts,
        SearchEngineService searchEngines,
        ILocalizationService loc)
        : base(strings)
    {
        _shortcuts = shortcuts;
        _searchEngines = searchEngines;
        _loc = loc;
    }

    public ObservableCollection<NewTabShortcut> Shortcuts { get; } = new();
    public ObservableCollection<SearchEngine> Engines { get; } = new();

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial SearchEngine? SelectedEngine { get; set; }
    [ObservableProperty] public partial string ClockText { get; set; } = string.Empty;
    [ObservableProperty] public partial string DateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string GreetingText { get; set; } = string.Empty;

    public BrowserSettings Settings => Tab?.Shell.Settings ?? new BrowserSettings();

    public override async Task LoadAsync(string? argument, CancellationToken ct = default)
    {
        Shortcuts.Clear();
        foreach (var shortcut in await _shortcuts.GetAllAsync(ct).ConfigureAwait(true))
            Shortcuts.Add(shortcut);

        Engines.Clear();
        foreach (var engine in await _searchEngines.GetAllAsync(ct).ConfigureAwait(true))
            Engines.Add(engine);
        SelectedEngine = Engines.FirstOrDefault(e => e.Id == Settings.DefaultSearchEngineId) ?? Engines.FirstOrDefault();

        RefreshClock();
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>Called once per second by the page's DispatcherTimer.</summary>
    public void RefreshClock()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm", _loc.Culture);
        DateText = now.ToString("D", _loc.Culture);

        var greeting = now.Hour switch
        {
            >= 5 and < 12 => Strings.NewtabGreetingMorning,
            >= 12 and < 17 => Strings.NewtabGreetingAfternoon,
            >= 17 and < 21 => Strings.NewtabGreetingEvening,
            _ => Strings.NewtabGreetingNight
        };
        var name = Settings.NewTabGreetingName?.Trim();
        GreetingText = string.IsNullOrEmpty(name) ? greeting : $"{greeting}، {name}";
        if (!_loc.IsRightToLeft && !string.IsNullOrEmpty(name))
            GreetingText = $"{greeting}, {name}";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = SearchText?.Trim();
        if (string.IsNullOrEmpty(text) || Tab is null) return;
        if (SelectedEngine is not null) Tab.SessionSearchEngineId = SelectedEngine.Id;
        await Tab.SubmitAddressAsync(text).ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenShortcut(NewTabShortcut? shortcut)
    {
        if (shortcut is not null) NavigateHere(shortcut.Url);
    }

    public async Task<bool> AddShortcutAsync(string title, string url)
    {
        var parsed = new AddressBarParser().Parse(url?.Trim() ?? string.Empty);
        if (parsed.Kind != AddressBarInputKind.Url) return false;
        var shortcut = new NewTabShortcut
        {
            Id = "shortcut-" + Guid.NewGuid().ToString("N")[..12],
            Title = string.IsNullOrWhiteSpace(title) ? parsed.Target : title.Trim(),
            Url = parsed.Target,
            IconUrl = UrlValidator.IsValidHttpUrl(parsed.Target, out var uri) && uri is not null
                ? $"{uri.Scheme}://{uri.Host}/favicon.ico"
                : null,
            SortOrder = Shortcuts.Count
        };
        await _shortcuts.UpsertAsync(shortcut).ConfigureAwait(true);
        Shortcuts.Add(shortcut);
        return true;
    }

    [RelayCommand]
    private async Task DeleteShortcutAsync(NewTabShortcut? shortcut)
    {
        if (shortcut is null) return;
        Shortcuts.Remove(shortcut);
        await _shortcuts.DeleteAsync(shortcut.Id).ConfigureAwait(true);
    }

    public async Task PersistOrderAsync()
    {
        await _shortcuts.SetSortOrderAsync(Shortcuts.Select(s => s.Id).ToList()).ConfigureAwait(true);
    }
}
