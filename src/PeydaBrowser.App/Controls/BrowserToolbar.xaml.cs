using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PeydaBrowser.App.Services;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;
using PeydaBrowser.WebEngine;
using Windows.ApplicationModel.DataTransfer;

namespace PeydaBrowser.App.Controls;

/// <summary>
/// The per-tab toolbar: navigation buttons, security indicator, omnibox with
/// suggestions, search-engine picker, bookmark star and the main menu.
/// Instantiated per tab so each tab keeps its own typed address text and
/// back/forward enablement (matching real browser behaviour).
/// </summary>
public sealed partial class BrowserToolbar : UserControl
{
    private BrowserTabViewModel? _vm;
    private bool _updatingAddressText;

    public BrowserToolbar()
    {
        InitializeComponent();
        AddressBox.ContextFlyout = BuildAddressContextFlyout();
    }

    private MainViewModel Shell => (MainViewModel)_vm!.Shell;
    private LocalizedStrings Strings => _vm!.Shell.Strings;

    public void Attach(BrowserTabViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.FocusAddressBarRequested += OnFocusAddressBarRequested;
        AddressBox.ItemsSource = vm.Suggestions;
        RefreshAll();
    }

    public void RefreshFromSettings() => RefreshAll();

    private void RefreshAll()
    {
        if (_vm is null) return;
        var shell = Shell;

        // Tooltips + accessibility names (no hard-coded UI text).
        ToolTipService.SetToolTip(BackButton, Strings.NavBack);
        ToolTipService.SetToolTip(ForwardButton, Strings.NavForward);
        ToolTipService.SetToolTip(ReloadButton, Strings.NavReload);
        ToolTipService.SetToolTip(StopButton, Strings.NavStop);
        ToolTipService.SetToolTip(HomeButton, Strings.NavHome);
        ToolTipService.SetToolTip(NewTabButton, Strings.NavNewTab);
        ToolTipService.SetToolTip(HistoryButton, Strings.MenuHistory);
        ToolTipService.SetToolTip(DownloadsButton, Strings.MenuDownloads);
        ToolTipService.SetToolTip(MenuButton, Strings.NavMenu);
        ToolTipService.SetToolTip(BookmarkButton, Strings.NavBookmark);
        AutomationProperties.SetName(BackButton, Strings.NavBack);
        AutomationProperties.SetName(ForwardButton, Strings.NavForward);
        AutomationProperties.SetName(ReloadButton, Strings.NavReload);
        AutomationProperties.SetName(HomeButton, Strings.NavHome);
        AutomationProperties.SetName(MenuButton, Strings.NavMenu);

        AddressBox.PlaceholderText = Strings.NavAddressPlaceholder;

        var rtl = shell.Localization.IsRightToLeft;
        // Mirror the back/forward chevrons in right-to-left layouts.
        (BackButton.Content as FontIcon)!.Glyph = rtl ? "\uE72A" : "\uE72B";
        (ForwardButton.Content as FontIcon)!.Glyph = rtl ? "\uE72B" : "\uE72A";

        PrivateBadge.Visibility = shell.IsPrivateWindow ? Visibility.Visible : Visibility.Collapsed;
        PrivateBadgeText.Text = Strings.PrivateBadge;

        var showSidebar = shell.Settings.ShowSidebar;
        HistoryButton.Visibility = showSidebar ? Visibility.Visible : Visibility.Collapsed;
        DownloadsButton.Visibility = showSidebar ? Visibility.Visible : Visibility.Collapsed;

        RefreshNavigationState();
        RefreshSecurity();
        RefreshBookmarkStar();
        SyncAddressText();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserTabViewModel.CanGoBack):
            case nameof(BrowserTabViewModel.CanGoForward):
            case nameof(BrowserTabViewModel.IsLoading):
                RefreshNavigationState();
                break;
            case nameof(BrowserTabViewModel.AddressText):
                SyncAddressText();
                break;
            case nameof(BrowserTabViewModel.SecurityInfo):
            case nameof(BrowserTabViewModel.CurrentUrl):
                RefreshSecurity();
                RefreshBookmarkStar();
                break;
            case nameof(BrowserTabViewModel.IsBookmarked):
                RefreshBookmarkStar();
                break;
        }
    }

    private void RefreshNavigationState()
    {
        if (_vm is null) return;
        var onWebPage = _vm.CurrentInternalPage is null;
        BackButton.IsEnabled = _vm.CanGoBack || _vm.CurrentInternalPage is not null;
        ForwardButton.IsEnabled = _vm.CanGoForward && onWebPage;
        ReloadButton.Visibility = _vm.IsLoading ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = _vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshSecurity()
    {
        if (_vm?.SecurityInfo is not { } info) return;
        if (info.IsInternal)
        {
            SecurityIcon.Glyph = "\uE946"; // info
            ToolTipService.SetToolTip(SecurityButton, Strings.NavInternalPage);
            SecurityTitle.Text = Strings.NavInternalPage;
            SecurityDetails.Text = _vm.CurrentUrl;
        }
        else if (info.IsSecure)
        {
            SecurityIcon.Glyph = "\uE72E"; // lock
            ToolTipService.SetToolTip(SecurityButton, Strings.NavSecure);
            SecurityTitle.Text = Strings.NavSecure;
            SecurityDetails.Text = info.DisplayHost;
        }
        else
        {
            SecurityIcon.Glyph = "\uE7BA"; // warning
            ToolTipService.SetToolTip(SecurityButton, Strings.NavNotSecure);
            SecurityTitle.Text = Strings.NavNotSecure;
            SecurityDetails.Text = info.DisplayHost;
        }
        if (info.IsIdnHomographRisk)
            SecurityDetails.Text += "\n" + info.DisplayHost;
    }

    private void RefreshBookmarkStar()
    {
        if (_vm is null) return;
        BookmarkIcon.Glyph = _vm.IsBookmarked ? "\uE735" : "\uE734";
        if (Application.Current.Resources.TryGetValue("PeydaAccentBrush", out var brush) && brush is Brush accent && _vm.IsBookmarked)
            BookmarkIcon.Foreground = accent;
        else
            BookmarkIcon.ClearValue(FontIcon.ForegroundProperty);
        ToolTipService.SetToolTip(BookmarkButton, _vm.IsBookmarked ? Strings.NavBookmarked : Strings.NavBookmark);
    }

    private void SyncAddressText()
    {
        if (_vm is null || _updatingAddressText) return;
        if (AddressBox.Text != _vm.AddressText)
        {
            _updatingAddressText = true;
            AddressBox.Text = _vm.AddressText;
            _updatingAddressText = false;
        }
    }

    private void OnFocusAddressBarRequested(object? sender, EventArgs e)
    {
        AddressBox.Focus(FocusState.Programmatic);
    }

    // ----- address box -----

    private void OnAddressTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_vm is null || _updatingAddressText) return;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _updatingAddressText = true;
            _vm.AddressText = sender.Text;
            _updatingAddressText = false;
        }
    }

    private async void OnAddressQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_vm is null) return;
        if (args.ChosenSuggestion is OmniboxSuggestion suggestion)
        {
            _vm.NavigateToUrl(suggestion.Url);
        }
        else
        {
            await _vm.SubmitAddressAsync(args.QueryText);
        }
        await _vm.View?.FocusWebContentAsync()!;
    }

    private void OnAddressSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (_vm is null || args.SelectedItem is not OmniboxSuggestion suggestion) return;
        _vm.SetAddressTextSilently(suggestion.Url);
        sender.Text = suggestion.Url;
    }

    private MenuFlyout BuildAddressContextFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opening += async (_, _) =>
        {
            flyout.Items.Clear();
            if (_vm is null) return;

            string? clipboardText = null;
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text))
                    clipboardText = await content.GetTextAsync();
            }
            catch (Exception)
            {
                // Clipboard may be unavailable; the paste entries are then omitted.
            }

            if (!string.IsNullOrWhiteSpace(clipboardText))
            {
                var text = clipboardText.Trim();
                var goItem = new MenuFlyoutItem { Text = Strings.OmniboxPasteAndGo };
                goItem.Click += async (_, _) => await _vm.SubmitAddressAsync(text);
                flyout.Items.Add(goItem);
                var searchItem = new MenuFlyoutItem { Text = Strings.OmniboxPasteAndSearch };
                searchItem.Click += async (_, _) => await _vm.SearchWithDefaultAsync(text);
                flyout.Items.Add(searchItem);
                flyout.Items.Add(new MenuFlyoutSeparator());
            }

            // Standard editing entries delegate to the inner TextBox so nothing
            // is lost by providing a custom omnibox context menu.
            var textBox = FindInnerTextBox();
            MenuFlyoutItem EditItem(string label, Action action, bool enabled)
            {
                var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
                item.Click += (_, _) => action();
                return item;
            }
            flyout.Items.Add(EditItem(Strings.CommonCut, () => textBox?.CutSelectionToClipboard(), textBox is not null));
            flyout.Items.Add(EditItem(Strings.CommonCopy, () => textBox?.CopySelectionToClipboard(), textBox is not null));
            flyout.Items.Add(EditItem(Strings.CommonPaste, () => textBox?.PasteFromClipboard(), textBox is not null));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(EditItem(Strings.CommonSelectAll, () => textBox?.SelectAll(), textBox is not null));
        };
        return flyout;
    }

    private TextBox? FindInnerTextBox()
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(AddressBox);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is TextBox textBox) return textBox;
            var children = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++)
                queue.Enqueue(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(current, i));
        }
        return null;
    }

    // ----- buttons -----

    private void OnBackClick(object sender, RoutedEventArgs e) => _vm?.GoBackCommand.Execute(null);
    private void OnForwardClick(object sender, RoutedEventArgs e) => _vm?.GoForwardCommand.Execute(null);
    private void OnReloadClick(object sender, RoutedEventArgs e) => _vm?.ReloadCommand.Execute(null);
    private void OnStopClick(object sender, RoutedEventArgs e) => _vm?.StopCommand.Execute(null);
    private void OnHomeClick(object sender, RoutedEventArgs e) => _vm?.GoHomeCommand.Execute(null);
    private void OnSecurityClick(object sender, RoutedEventArgs e) => RefreshSecurity();

    private async void OnBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null || XamlRoot is null) return;
        await _vm.Shell.ToggleBookmarkAsync(_vm, XamlRoot);
        RefreshBookmarkStar();
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e) => Shell.OpenInternalPageSmart(InternalPage.History);
    private void OnDownloadsClick(object sender, RoutedEventArgs e) => Shell.OpenInternalPageSmart(InternalPage.Downloads);
    private void OnNewTabClick(object sender, RoutedEventArgs e) => Shell.OpenNewTab(null, activate: true);

    // ----- engine flyout -----

    private void OnEngineFlyoutOpening(object sender, object e)
    {
        EngineFlyout.Items.Clear();
        if (_vm is null) return;
        var activeId = _vm.SessionSearchEngineId ?? Shell.Settings.DefaultSearchEngineId;
        foreach (var engine in Shell.SearchEngines)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = SearchUrlBuilder.IsSecureTemplate(engine.UrlTemplate)
                    ? engine.Name
                    : $"{engine.Name} ({Strings.SearchEngineInsecureShort})",
                GroupName = "searchEngines",
                IsChecked = engine.Id == activeId,
                Tag = engine.Id
            };
            item.Click += (_, _) => _vm.SessionSearchEngineId = engine.Id;
            EngineFlyout.Items.Add(item);
        }
    }

    // ----- main menu -----

    private void OnMainMenuOpening(object sender, object e)
    {
        MainMenu.Items.Clear();
        if (_vm is null) return;
        var s = Strings;

        MenuFlyoutItem Item(string text, string? glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            if (glyph is not null) item.Icon = new FontIcon { Glyph = glyph };
            item.Click += (_, _) => action();
            return item;
        }

        MainMenu.Items.Add(Item(s.NavNewTab, "\uE710", () => Shell.OpenNewTab(null, true)));
        MainMenu.Items.Add(Item(s.MenuNewPrivateWindow, "\uE72E", () => Shell.OpenPrivateWindow()));
        MainMenu.Items.Add(new MenuFlyoutSeparator());
        MainMenu.Items.Add(Item(s.MenuHistory, "\uE81C", () => Shell.OpenInternalPageSmart(InternalPage.History)));
        MainMenu.Items.Add(Item(s.MenuDownloads, "\uE896", () => Shell.OpenInternalPageSmart(InternalPage.Downloads)));
        MainMenu.Items.Add(Item(s.MenuBookmarks, "\uE734", () => Shell.OpenInternalPageSmart(InternalPage.Bookmarks)));
        MainMenu.Items.Add(new MenuFlyoutSeparator());
        MainMenu.Items.Add(Item(s.MenuZoomIn, "\uE8C8", () => _vm.ZoomIn()));
        MainMenu.Items.Add(Item(s.MenuZoomOut, "\uE8C9", () => _vm.ZoomOut()));
        MainMenu.Items.Add(Item(s.MenuZoomReset, null, () => _vm.ZoomReset()));
        MainMenu.Items.Add(Item(s.MenuPrint, "\uE749", () => _vm.View?.Print()));
        MainMenu.Items.Add(new MenuFlyoutSeparator());
        MainMenu.Items.Add(Item(s.MenuSettings, "\uE713", () => Shell.OpenInternalPageSmart(InternalPage.Settings)));
        MainMenu.Items.Add(Item(s.MenuAbout, "\uE946", () => Shell.OpenInternalPageSmart(InternalPage.About)));
        MainMenu.Items.Add(new MenuFlyoutSeparator());
        MainMenu.Items.Add(Item(s.MenuExit, "\uE7E8", () => Shell.CloseWindowAction?.Invoke()));
    }
}
