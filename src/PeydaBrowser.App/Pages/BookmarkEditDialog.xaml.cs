using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.Services;
using PeydaBrowser.Core.Models;

namespace PeydaBrowser.App.Pages;

/// <summary>Add/edit bookmark dialog. Read the values after ShowAsync returns Primary.</summary>
public sealed partial class BookmarkEditDialog : ContentDialog
{
    public BookmarkEditDialog(LocalizedStrings strings, string title, string url,
        IReadOnlyList<BookmarkFolder> folders, string? selectedFolderId = null, bool isEdit = false)
    {
        InitializeComponent();

        Title = isEdit ? strings.BookmarksEditTitle : strings.BookmarksAddTitle;
        PrimaryButtonText = strings.CommonSave;
        CloseButtonText = strings.CommonCancel;
        DefaultButton = ContentDialogButton.Primary;

        TitleBox.Header = strings.CommonName;
        TitleBox.Text = title;
        UrlBox.Header = strings.CommonUrl;
        UrlBox.Text = url;
        FolderCombo.Header = strings.CommonFolder;

        FolderCombo.Items.Add(new ComboBoxItem { Content = strings.CommonNone, Tag = null });
        foreach (var folder in folders)
            FolderCombo.Items.Add(new ComboBoxItem { Content = folder.Name, Tag = folder.Id });

        FolderCombo.SelectedIndex = 0;
        if (selectedFolderId is not null)
        {
            for (var i = 0; i < FolderCombo.Items.Count; i++)
            {
                if (FolderCombo.Items[i] is ComboBoxItem item && (item.Tag as string) == selectedFolderId)
                {
                    FolderCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    public string BookmarkTitle => TitleBox.Text;
    public string BookmarkUrl => UrlBox.Text;
    public string? SelectedFolderId => (FolderCombo.SelectedItem as ComboBoxItem)?.Tag as string;
}
