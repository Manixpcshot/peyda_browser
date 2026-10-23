using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.Services;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.App.Pages;

public sealed partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;
    private MainViewModel? Shell => ViewModel?.Tab?.Shell as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var s = ViewModel.Strings;

        StartupCombo.Items.Clear();
        StartupCombo.Items.Add(s.SettingsStartupRestore);
        StartupCombo.Items.Add(s.SettingsStartupNewTab);
        StartupCombo.Items.Add(s.SettingsStartupHome);

        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(s.SettingsLanguageEn);
        LanguageCombo.Items.Add(s.SettingsLanguageFa);

        ThemeModeCombo.Items.Clear();
        ThemeModeCombo.Items.Add(s.SettingsThemeSystem);
        ThemeModeCombo.Items.Add(s.SettingsThemeLight);
        ThemeModeCombo.Items.Add(s.SettingsThemeDark);

        DensityCombo.Items.Clear();
        DensityCombo.Items.Add(s.SettingsDensityCompact);
        DensityCombo.Items.Add(s.SettingsDensityNormal);
        DensityCombo.Items.Add(s.SettingsDensityComfortable);

        ChannelCombo.Items.Clear();
        ChannelCombo.Items.Add("Stable");
        ChannelCombo.Items.Add("Beta");
        ChannelCombo.Items.Add("Nightly");

        // Re-apply two-way selections now that items exist.
        StartupCombo.SelectedIndex = ViewModel.StartupIndex;
        LanguageCombo.SelectedIndex = ViewModel.LanguageIndex;
        ThemeModeCombo.SelectedIndex = ViewModel.ThemeModeIndex;
        DensityCombo.SelectedIndex = ViewModel.DensityIndex;
        ChannelCombo.SelectedIndex = ViewModel.UpdateChannelIndex;
    }

    private void OnSettingChanged(object sender, SelectionChangedEventArgs e) => ViewModel?.QueueSave();
    private void OnToggleChanged(object sender, RoutedEventArgs e) => ViewModel?.QueueSave();
    private void OnSettingLostFocus(object sender, RoutedEventArgs e) => ViewModel?.QueueSave();

    private async void OnEngineMoveUp(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SearchEngine engine } && ViewModel is not null)
            await ViewModel.MoveEngineAsync(engine, -1);
    }

    private async void OnEngineMoveDown(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SearchEngine engine } && ViewModel is not null)
            await ViewModel.MoveEngineAsync(engine, +1);
    }

    private async void OnEngineDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SearchEngine engine } && ViewModel is not null)
            await ViewModel.DeleteEngineCommand.ExecuteAsync(engine);
    }

    private async void OnPermissionDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SitePermission permission } && ViewModel is not null)
            await ViewModel.DeletePermissionCommand.ExecuteAsync(permission);
    }

    private async void OnImportThemeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Shell?.WindowHandleProvider is null) return;
        var path = await PickerHelper.PickSingleFileAsync(Shell.WindowHandleProvider(), ".json");
        if (path is not null) await ViewModel.ImportThemeAsync(path);
    }

    private async void OnExportThemeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Shell?.WindowHandleProvider is null) return;
        var path = await PickerHelper.PickSaveFileAsync(Shell.WindowHandleProvider(), "peyda-theme.json", "Peyda Theme", ".json");
        if (path is not null) await ViewModel.ExportThemeAsync(path);
    }

    private async void OnBrowseDownloadFolder(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Shell?.WindowHandleProvider is null) return;
        var folder = await PickerHelper.PickFolderAsync(Shell.WindowHandleProvider());
        if (folder is not null)
        {
            ViewModel.DownloadFolder = folder;
            ViewModel.QueueSave();
        }
    }

    private async void OnChooseBackgroundImage(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Shell?.WindowHandleProvider is null) return;
        var path = await PickerHelper.PickSingleFileAsync(Shell.WindowHandleProvider(), ".png", ".jpg", ".jpeg", ".bmp", ".webp");
        if (path is not null)
        {
            ViewModel.NtBackgroundImage = path;
            ViewModel.QueueSave();
        }
    }

    private void OnClearBackgroundImage(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.NtBackgroundImage = string.Empty;
        ViewModel.QueueSave();
    }

    private async void OnClearDataClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || XamlRoot is null) return;
        var s = ViewModel.Strings;

        var rangeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        rangeBox.Items.Add(s.ClearLastHour);
        rangeBox.Items.Add(s.ClearLast24Hours);
        rangeBox.Items.Add(s.ClearLast7Days);
        rangeBox.Items.Add(s.ClearLast4Weeks);
        rangeBox.Items.Add(s.ClearAllTime);
        rangeBox.SelectedIndex = 1;
        rangeBox.Header = s.ClearRange;

        var historyCheck = new CheckBox { Content = s.ClearHistory, IsChecked = true };
        var siteDataCheck = new CheckBox { Content = s.ClearSiteData, IsChecked = true };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(rangeBox);
        panel.Children.Add(historyCheck);
        panel.Children.Add(siteDataCheck);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = s.ClearTitle,
            Content = panel,
            PrimaryButtonText = s.ClearConfirm,
            CloseButtonText = s.CommonCancel,
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var range = rangeBox.SelectedIndex switch
        {
            0 => ClearTimeRange.LastHour,
            2 => ClearTimeRange.Last7Days,
            3 => ClearTimeRange.Last4Weeks,
            4 => ClearTimeRange.AllTime,
            _ => ClearTimeRange.Last24Hours
        };
        await ViewModel.ClearDataAsync(range,
            historyCheck.IsChecked == true,
            siteDataCheck.IsChecked == true);
        ClearStatus.Text = s.ClearDone;
    }
}
