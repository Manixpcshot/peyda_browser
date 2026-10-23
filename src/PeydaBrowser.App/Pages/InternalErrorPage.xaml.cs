using Microsoft.UI.Xaml.Controls;

namespace PeydaBrowser.App.Pages;

/// <summary>Shown for unknown peyda:// pages. DataContext is LocalizedStrings.</summary>
public sealed partial class InternalErrorPage : UserControl
{
    public InternalErrorPage() => InitializeComponent();
}
