using EmailAI.UI.Shared.Abstractions;

namespace EmailAI.MAUI.Services;

public sealed class MauiFilePickerService : IFilePickerService
{
    public async Task<string?> PickDatabaseFileAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select SQLite database",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".db"],
                [DevicePlatform.MacCatalyst] = ["db"],
                [DevicePlatform.iOS] = ["public.database"],
                [DevicePlatform.Android] = ["*/*"]
            })
        });

        return result?.FullPath;
    }
}
