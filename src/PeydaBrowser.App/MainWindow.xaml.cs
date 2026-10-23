using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PeydaBrowser.App.Controls;
using PeydaBrowser.App.Services;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Shortcuts;
using Windows.Graphics;
using Windows.System;

namespace PeydaBrowser.App;

/// <summary>
/// The browser window: custom title bar (content extended, system caption
/// buttons recolored to match the theme), a reorderable TabView whose items
/// are kept in sync with MainViewModel.Tabs, per-tab toolbar + bookmarks bar
/// + WebView2 host, keyboard accelerators and session saving on close.
/// </summary>
public sealed partial class MainWindow : Window
{
    private sealed class TabVisual
    {
        public required TabViewItem Item { get; init; }
        public required TextBlock TitleText { get; init; }
        public required Image FaviconImage { get; init; }
        public required FontIcon MuteIcon { get; init; }
        public required FontIcon FallbackIcon { get; init; }
        public required BrowserToolbar Toolbar { get; init; }
        public required Border BookmarkBarHost { get; init; }
        public required StackPanel BookmarkBarPanel { get; init; }
        public required BrowserTabView TabView { get; init; }
        public required MenuFlyout ContextMenu { get; init; }
    }

    private readonly MainViewModel _vm;
    private readonly string? _initialUrl;
    private readonly Dictionary<Guid, TabVisual> _visuals = new();
    private bool _syncingSelection;

    public MainWindow(MainViewModel viewModel, string? initialUrl)
    {
        InitializeComponent();
        _vm = viewModel;
        _initialUrl = initialUrl;

        ConfigureWindowChrome();
        WireShellCallbacks();
        BuildKeyboardAccelerators();

        RootGrid.DataContext = _vm;
        _vm.Tabs.CollectionChanged += OnTabsCollectionChanged;
        _vm.BookmarkBarItems.CollectionChanged += OnBookmarkBarChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.SettingsChanged += OnSettingsChanged;

        RootGrid.Loaded += OnRootLoaded;
        AppWindow.Closing += OnAppWindowClosing;
    }

    public MainViewModel ViewModel => _vm;

    private void ConfigureWindowChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"));
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var width = Math.Min(1320, area.WorkArea.Width - 80);
            var height = Math.Min(860, area.WorkArea.Height - 60);
            var x = area.WorkArea.X + (area.WorkArea.Width - width) / 2;
            var y = area.WorkArea.Y + (area.WorkArea.Height - height) / 2;
            AppWindow.MoveAndResize(new RectInt32(x, y, Math.Max(900, width), Math.Max(600, height)));
        }
        catch (Exception)
        {
            // Icon/initial placement is cosmetic; never block startup on it.
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 760;
            presenter.PreferredMinimumHeight = 480;
        }
    }

    private void WireShellCallbacks()
    {
        _vm.WindowHandleProvider = () => WinRT.Interop.WindowNative.GetWindowHandle(this);
        _vm.XamlRootProvider = () => RootGrid.XamlRoot;
        _vm.SetFullscreenAction = fullscreen =>
            AppWindow.SetPresenter(fullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        _vm.CloseWindowAction = Close;
    }

    private void BuildKeyboardAccelerators()
    {
        foreach (var definition in DefaultShortcuts.All)
        {
            if (!KeyboardShortcutRouter.TryToVirtualKey(definition.Gesture, out var key)) continue;
            var modifiers = VirtualKeyModifiers.None;
            if (definition.Gesture.Ctrl) modifiers |= VirtualKeyModifiers.Control;
            if (definition.Gesture.Alt) modifiers |= VirtualKeyModifiers.Menu;
            if (definition.Gesture.Shift) modifiers |= VirtualKeyModifiers.Shift;

            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            var captured = definition;
            accelerator.Invoked += async (_, args) =>
            {
                args.Handled = true;
                await _vm.ExecuteShortcutAsync(captured.Command, captured.Argument);
            };
            RootGrid.KeyboardAccelerators.Add(accelerator);
        }
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ApplyShellSettings();
        await _vm.InitializeTabsAsync(_initialUrl);
    }

    private bool _closeAfterSave;

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // AppWindowClosingEventArgs has no deferral: cancel the first close,
        // save the session asynchronously, then close for real.
        if (_vm.IsPrivateWindow || _closeAfterSave) return;
        args.Cancel = true;
        _closeAfterSave = true;
        try
        {
            await _vm.SaveSessionAsync();
        }
        catch (Exception)
        {
            // Never trap the user in an unclosable window because saving failed.
        }
        Close();
    }

    // ============================================================ settings / theme

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyShellSettings();
        foreach (var visual in _visuals.Values)
        {
            visual.Toolbar.RefreshFromSettings();
            RefreshBookmarkBar(visual);
        }
        foreach (var tab in _vm.Tabs)
            tab.View?.ApplyPreferredColorScheme(_vm.ThemeService.IsDarkEffective);
    }

    private void ApplyShellSettings()
    {
        _vm.ThemeService.Apply(_vm.Settings, RootGrid);
        RootGrid.FlowDirection = _vm.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var foreground = _vm.ThemeService.IsDarkEffective ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;

        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        var pageTitle = _vm.SelectedTab?.DisplayTitle;
        Title = string.IsNullOrWhiteSpace(pageTitle)
            ? "Peyda Browser"
            : $"{pageTitle} — Peyda Browser";
        TitleBarText.Text = _vm.IsPrivateWindow
            ? $"Peyda Browser — {_vm.Strings.PrivateBadge}"
            : "Peyda Browser";
    }

    // ============================================================ tab visuals sync

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            SyncSelectionFromViewModel();
            UpdateWindowTitle();
        }
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (var item in e.NewItems.Cast<BrowserTabViewModel>())
                BrowserTabs.TabItems.Add(CreateTabVisual(item).Item);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            foreach (var item in e.OldItems.Cast<BrowserTabViewModel>())
            {
                if (_visuals.TryGetValue(item.Id, out var visual))
                {
                    BrowserTabs.TabItems.Remove(visual.Item);
                    _visuals.Remove(item.Id);
                }
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Move)
        {
            // Order is re-synced from the TabView in OnTabStripDrop.
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            BrowserTabs.TabItems.Clear();
            _visuals.Clear();
            foreach (var tab in _vm.Tabs)
                BrowserTabs.TabItems.Add(CreateTabVisual(tab).Item);
        }
        SyncSelectionFromViewModel();
        UpdateWindowTitle();
    }

    private TabVisual CreateTabVisual(BrowserTabViewModel tab)
    {
        var titleText = new TextBlock
        {
            Text = tab.DisplayTitle,
            MaxWidth = 170,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var faviconImage = new Image { Width = 16, Height = 16, Visibility = Visibility.Collapsed };
        var fallbackIcon = new FontIcon { Glyph = "\uE774", FontSize = 13 };
        var muteIcon = new FontIcon { Glyph = "\uE74F", FontSize = 11, Visibility = Visibility.Collapsed };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(faviconImage);
        header.Children.Add(fallbackIcon);
        header.Children.Add(titleText);
        header.Children.Add(muteIcon);

        var toolbar = new BrowserToolbar();
        toolbar.Attach(tab);

        var barPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var barScroll = new ScrollViewer
        {
            Content = barPanel,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(6, 2, 6, 2)
        };
        var barHost = new Border { Child = barScroll };

        var tabView = new BrowserTabView();
        tabView.Attach(tab);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(barHost, 1);
        Grid.SetRow(tabView, 2);
        content.Children.Add(toolbar);
        content.Children.Add(barHost);
        content.Children.Add(tabView);

        var contextMenu = BuildTabContextMenu(tab);
        var item = new TabViewItem
        {
            Header = header,
            Content = content,
            IsClosable = !tab.IsPinned,
            ContextFlyout = contextMenu,
            Tag = tab.Id
        };

        var visual = new TabVisual
        {
            Item = item,
            TitleText = titleText,
            FaviconImage = faviconImage,
            MuteIcon = muteIcon,
            FallbackIcon = fallbackIcon,
            Toolbar = toolbar,
            BookmarkBarHost = barHost,
            BookmarkBarPanel = barPanel,
            TabView = tabView,
            ContextMenu = contextMenu
        };
        _visuals[tab.Id] = visual;

        tab.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(BrowserTabViewModel.DisplayTitle):
                case nameof(BrowserTabViewModel.Title):
                    titleText.Text = tab.DisplayTitle;
                    if (_vm.SelectedTab == tab) UpdateWindowTitle();
                    break;
                case nameof(BrowserTabViewModel.Favicon):
                    faviconImage.Source = tab.Favicon;
                    faviconImage.Visibility = tab.Favicon is null ? Visibility.Collapsed : Visibility.Visible;
                    fallbackIcon.Visibility = tab.Favicon is null ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(BrowserTabViewModel.IsMuted):
                    muteIcon.Visibility = tab.IsMuted ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(BrowserTabViewModel.IsPinned):
                    item.IsClosable = !tab.IsPinned;
                    titleText.Visibility = tab.IsPinned ? Visibility.Collapsed : Visibility.Visible;
                    break;
            }
        };

        RefreshBookmarkBar(visual);
        return visual;
    }

    private void SyncSelectionFromViewModel()
    {
        if (_syncingSelection || _vm.SelectedTab is null) return;
        if (_visuals.TryGetValue(_vm.SelectedTab.Id, out var visual) && BrowserTabs.SelectedItem != visual.Item)
        {
            _syncingSelection = true;
            BrowserTabs.SelectedItem = visual.Item;
            _syncingSelection = false;
        }
    }

    private BrowserTabViewModel? TabOf(TabViewItem? item) =>
        item?.Tag is Guid id ? _vm.Tabs.FirstOrDefault(t => t.Id == id) : null;

    // ============================================================ TabView events

    private void OnAddTabButtonClick(TabView sender, object args)
    {
        _vm.OpenNewTab(null, activate: true);
        _vm.SelectedTab?.RequestFocusAddressBar();
    }

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (TabOf(args.Tab) is { } tab)
            await _vm.CloseTabAsync(tab);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (BrowserTabs.SelectedItem is TabViewItem item && TabOf(item) is { } tab)
        {
            _syncingSelection = true;
            _vm.SelectedTab = tab;
            _syncingSelection = false;
            UpdateWindowTitle();
        }
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        var ordered = BrowserTabs.TabItems
            .Cast<TabViewItem>()
            .Select(TabOf)
            .Where(t => t is not null)
            .Cast<BrowserTabViewModel>()
            .ToList();
        _vm.SyncTabOrder(ordered);
    }

    // ============================================================ tab context menu

    private MenuFlyout BuildTabContextMenu(BrowserTabViewModel tab)
    {
        var menu = new MenuFlyout();
        var s = _vm.Strings;

        MenuFlyoutItem Item(string text, Func<Task> action)
        {
            var entry = new MenuFlyoutItem { Text = text };
            entry.Click += async (_, _) => await action();
            return entry;
        }

        var pinItem = new MenuFlyoutItem();
        pinItem.Click += (_, _) =>
        {
            tab.IsPinned = !tab.IsPinned;
            _vm.NotifySessionDirty();
        };
        var muteItem = new MenuFlyoutItem();
        muteItem.Click += (_, _) => tab.IsMuted = !tab.IsMuted;

        menu.Items.Add(Item(s.TabDuplicate, () => { _vm.DuplicateTab(tab); return Task.CompletedTask; }));
        menu.Items.Add(pinItem);
        menu.Items.Add(muteItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(s.TabReopenClosed, () => _vm.ReopenClosedTabAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(s.TabClose, () => _vm.CloseTabAsync(tab)));
        menu.Items.Add(Item(s.TabCloseOthers, () => _vm.CloseOtherTabsAsync(tab)));
        menu.Items.Add(Item(s.TabCloseRight, () => _vm.CloseTabsToRightAsync(tab)));

        menu.Opening += (_, _) =>
        {
            pinItem.Text = tab.IsPinned ? s.TabUnpin : s.TabPin;
            muteItem.Text = tab.IsMuted ? s.TabUnmute : s.TabMute;
        };
        return menu;
    }

    // ============================================================ bookmarks bar

    private void OnBookmarkBarChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var visual in _visuals.Values)
            RefreshBookmarkBar(visual);
    }

    private void RefreshBookmarkBar(TabVisual visual)
    {
        visual.BookmarkBarHost.Visibility = _vm.Settings.ShowBookmarkBar
            ? Visibility.Visible
            : Visibility.Collapsed;
        visual.BookmarkBarPanel.Children.Clear();
        foreach (var entry in _vm.BookmarkBarItems)
            visual.BookmarkBarPanel.Children.Add(BuildBookmarkBarButton(entry));
    }

    private Button BuildBookmarkBarButton(BookmarkBarItem entry)
    {
        var button = new Button
        {
            Content = entry.Title,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 3, 8, 3),
            Tag = entry
        };
        ToolTipService.SetToolTip(button, entry.Url ?? entry.Title);

        if (entry.IsFolder)
        {
            var flyout = new MenuFlyout();
            foreach (var child in entry.Children)
            {
                var childUrl = child.Url;
                var childItem = new MenuFlyoutItem { Text = child.Title };
                childItem.Click += (_, _) => { if (childUrl is not null) _vm.SelectedTab?.NavigateToUrl(childUrl); };
                flyout.Items.Add(childItem);
            }
            button.Flyout = flyout;
        }
        else
        {
            button.Click += (_, _) => { if (entry.Url is not null) _vm.SelectedTab?.NavigateToUrl(entry.Url); };
            var context = new MenuFlyout();
            var openNew = new MenuFlyoutItem { Text = _vm.Strings.TabNew };
            openNew.Click += (_, _) => { if (entry.Url is not null) _vm.OpenNewTab(entry.Url, activate: true); };
            context.Items.Add(openNew);
            button.ContextFlyout = context;
        }
        return button;
    }
}
