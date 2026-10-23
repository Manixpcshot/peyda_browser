using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeydaBrowser.App.Services;
using PeydaBrowser.App.ViewModels;
using PeydaBrowser.Core.Interfaces;
using PeydaBrowser.Core.Models;
using PeydaBrowser.Core.Services;

namespace PeydaBrowser.App.Pages;

public sealed partial class BookmarksPage : UserControl
{
    public BookmarksPage() => InitializeComponent();

    private BookmarksViewModel? ViewModel => DataContext as BookmarksViewModel;
    private MainViewModel? Shell => ViewModel?.Tab?.Shell as MainViewModel;

    private void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null) return;
        if (FolderList.SelectedItem is BookmarkFolder folder)
            ViewModel.SelectedFolderId = folder.Id;
    }

    private void OnBookmarkClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Bookmark bookmark)
            ViewModel?.OpenBookmarkCommand.Execute(bookmark);
    }

    private async void OnDeleteBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Bookmark bookmark } && ViewModel is not null)
            await ViewModel.DeleteBookmarkCommand.ExecuteAsync(bookmark);
    }

    private async void OnEditBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Bookmark bookmark } || ViewModel is null || XamlRoot is null || Shell is null) return;
        var repo = Shell.Services.GetService(typeof(IBookmarkRepository)) as IBookmarkRepository;
        var folders = repo is null ? Array.Empty<BookmarkFolder>() : await repo.GetFoldersAsync();
        var dialog = new BookmarkEditDialog(ViewModel.Strings, bookmark.Title, bookmark.Url, folders, bookmark.FolderId, isEdit: true)
        {
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        bookmark.Title = dialog.BookmarkTitle.Trim();
        bookmark.Url = dialog.BookmarkUrl.Trim();
        bookmark.FolderId = dialog.SelectedFolderId;
        await ViewModel.UpdateBookmarkAsync(bookmark);
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || XamlRoot is null) return;
        var nameBox = new TextBox { Header = ViewModel.Strings.CommonName };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ViewModel.Strings.BookmarksAddFolder,
            Content = nameBox,
            PrimaryButtonText = ViewModel.Strings.CommonAdd,
            CloseButtonText = ViewModel.Strings.CommonCancel,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var parentId = ViewModel.SelectedFolderId == BookmarksViewModel.AllBookmarksId ? null : ViewModel.SelectedFolderId;
            await ViewModel.AddFolderAsync(nameBox.Text, parentId);
        }
    }

    private async void OnDeleteFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (FolderList.SelectedItem is BookmarkFolder folder && folder.Id != BookmarksViewModel.AllBookmarksId)
            await ViewModel.DeleteFolderAsync(folder);
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Shell?.WindowHandleProvider is null) return;
        var path = await PickerHelper.PickSingleFileAsync(Shell.WindowHandleProvider(), ".html", ".htm");
        if (path is null) return;
        var html = await File.ReadAllTextAsync(path);
        var result = BookmarkHtmlService.Import(html);
        await ViewModel.ImportAsync(result);
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || Shell?.WindowHandleProvider is null) return;
        var repo = Shell.Services.GetService(typeof(IBookmarkRepository)) as IBookmarkRepository;
        if (repo is null) return;
        var path = await PickerHelper.PickSaveFileAsync(Shell.WindowHandleProvider(), "peyda-bookmarks.html", "HTML", ".html");
        if (path is null) return;
        var html = BookmarkHtmlService.Export(await repo.GetAllAsync(), await repo.GetFoldersAsync());
        await File.WriteAllTextAsync(path, html);
    }
}
