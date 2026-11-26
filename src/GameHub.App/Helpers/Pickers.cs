using Windows.Storage;
using Windows.Storage.Pickers;

namespace GameHub.App.Helpers;

/// <summary>Seletores de arquivo do Windows ligados à janela principal (app não empacotado).</summary>
public static class Pickers
{
    private static nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!);

    public static async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.ComputerFolder };
        foreach (var e in extensions) picker.FileTypeFilter.Add(e);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static Task<string?> PickExeAsync() => PickFileAsync(".exe");

    public static Task<string?> PickImageAsync() =>
        PickFileAsync(".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".ico");

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static async Task<string?> SaveFileAsync(string suggestedName, string typeLabel, string extension)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(typeLabel, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
