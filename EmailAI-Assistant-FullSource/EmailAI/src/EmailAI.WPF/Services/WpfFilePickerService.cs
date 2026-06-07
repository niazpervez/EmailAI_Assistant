using EmailAI.UI.Shared.Abstractions;
using Microsoft.Win32;

namespace EmailAI.WPF.Services;

public sealed class WpfFilePickerService : IFilePickerService
{
    public Task<string?> PickDatabaseFileAsync()
    {
        string? path = null;
        var dlg = new OpenFileDialog
        {
            Filter = "SQLite Database|*.db|All Files|*.*",
            Title = "Select Database Location"
        };

        if (dlg.ShowDialog() == true)
            path = dlg.FileName;

        return Task.FromResult(path);
    }
}
