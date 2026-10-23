using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Models;
using Windows.System;

namespace PeydaBrowser.App.Pages;

/// <summary>The local New Tab page. Fully offline: clock, date, greeting,
/// central search and user shortcuts are rendered natively.</summary>
public sealed partial class NewTabPage : UserControl
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public NewTabPage()
    {
        InitializeComponent();
        _clockTimer.Tick += (_, _) => ViewModel?.RefreshClock();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _clockTimer.Stop();
        ShortcutGrid.RightTapped += OnGridRightTapped;
    }

    private NewTabViewModel? ViewModel => DataContext as NewTabViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _clockTimer.Start();
        var path = ViewModel?.Settings.NewTabBackgroundImagePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                BackgroundImage.Source = new BitmapImage(new Uri(path));
            }
            catch (Exception)
            {
                BackgroundImage.Source = null;
            }
        }
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel is not null)
        {
            e.Handled = true;
            await ViewModel.SearchCommand.ExecuteAsync(null);
        }
    }

    private void OnShortcutClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NewTabShortcut shortcut)
            ViewModel?.OpenShortcutCommand.Execute(shortcut);
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Reordering only; do not advertise drag data to other apps.
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private async void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (ViewModel is not null)
            await ViewModel.PersistOrderAsync();
    }

    private void OnGridRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.OriginalSource is not FrameworkElement element) return;
        var item = element.DataContext as NewTabShortcut;
        if (item is null) return;

        var flyout = new MenuFlyout();
        var delete = new MenuFlyoutItem { Text = ViewModel.Strings.CommonDelete };
        delete.Click += async (_, _) => await ViewModel.DeleteShortcutCommand.ExecuteAsync(item);
        flyout.Items.Add(delete);
        flyout.ShowAt(element, e.GetPosition(element));
        e.Handled = true;
    }

    private async void OnAddShortcutClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || XamlRoot is null) return;
        var strings = ViewModel.Strings;
        var titleBox = new TextBox { Header = strings.CommonName };
        var urlBox = new TextBox { Header = strings.CommonUrl, FlowDirection = FlowDirection.LeftToRight };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(titleBox);
        panel.Children.Add(urlBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = strings.NewtabAddShortcut,
            Content = panel,
            PrimaryButtonText = strings.CommonAdd,
            CloseButtonText = strings.CommonCancel,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.AddShortcutAsync(titleBox.Text, urlBox.Text);
    }
}
