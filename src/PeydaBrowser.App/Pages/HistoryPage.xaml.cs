using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.App.Pages;

public sealed partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();

    private HistoryViewModel? ViewModel => DataContext as HistoryViewModel;

    private void OnEntryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HistoryEntry entry)
            ViewModel?.OpenEntryCommand.Execute(entry);
    }

    private async void OnDeleteEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntry entry } && ViewModel is not null)
            await ViewModel.DeleteEntryCommand.ExecuteAsync(entry);
    }

    private void OnClearFlyoutOpening(object sender, object e)
    {
        ClearFlyout.Items.Clear();
        if (ViewModel is null) return;
        var s = ViewModel.Strings;

        MenuFlyoutItem Range(string text, ClearTimeRange range)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += async (_, _) => await ViewModel.ClearAsync(range);
            return item;
        }

        ClearFlyout.Items.Add(Range(s.ClearLastHour, ClearTimeRange.LastHour));
        ClearFlyout.Items.Add(Range(s.ClearLast24Hours, ClearTimeRange.Last24Hours));
        ClearFlyout.Items.Add(Range(s.ClearLast7Days, ClearTimeRange.Last7Days));
        ClearFlyout.Items.Add(Range(s.ClearLast4Weeks, ClearTimeRange.Last4Weeks));
        ClearFlyout.Items.Add(new MenuFlyoutSeparator());
        ClearFlyout.Items.Add(Range(s.ClearAllTime, ClearTimeRange.AllTime));
    }
}
