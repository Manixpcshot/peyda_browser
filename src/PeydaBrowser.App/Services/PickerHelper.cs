using Windows.Storage;
using Windows.Storage.Pickers;

namespace PeydaBrowser.App.Services;

/// <summary>File/folder pickers initialized with the owning window handle
/// (required for unpackaged WinUI 3 apps).</summary>
public static class PickerHelper
{
    public static async Task<string?> PickSingleFileAsync(IntPtr hwnd, params string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
        if (picker.FileTypeFilter.Count == 0) picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickSaveFileAsync(IntPtr hwnd, string suggestedName, string description, string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName)
        };
        picker.FileTypeChoices.Add(description, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickFolderAsync(IntPtr hwnd)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
