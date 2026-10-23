using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.ViewModels;

namespace PeydaBrowser.App.Pages;

public sealed partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();

    private void OnOpenRepositoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel vm)
            vm.Tab?.Shell.OpenNewTab("https://github.com/Manixpcshot/peyda_browser", activate: true);
    }
}
