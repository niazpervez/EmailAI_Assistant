using EmailAI.UI.Shared.Abstractions;

namespace EmailAI.MAUI.Services;

public sealed class MauiMessageService : IMessageService
{
    public async Task ShowAlertAsync(string message, string title, MessageIcon icon = MessageIcon.Information)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
            await Microsoft.Maui.Controls.Application.Current!.MainPage!.DisplayAlert(title, message, "OK"));
    }

    public Task<bool> ShowConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
            await Microsoft.Maui.Controls.Application.Current!.MainPage!.DisplayAlert(title, message, "Yes", "No"));
    }
}
