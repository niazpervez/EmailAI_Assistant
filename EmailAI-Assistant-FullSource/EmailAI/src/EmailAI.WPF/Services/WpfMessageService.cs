using EmailAI.UI.Shared.Abstractions;
using System.Windows;

namespace EmailAI.WPF.Services;

public sealed class WpfMessageService : IMessageService
{
    public Task ShowAlertAsync(string message, string title, MessageIcon icon = MessageIcon.Information)
    {
        InvokeOnUi(() =>
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, ToImage(icon));
        });
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question)
    {
        var result = false;
        InvokeOnUi(() =>
        {
            result = MessageBox.Show(message, title, MessageBoxButton.YesNo, ToImage(icon)) == MessageBoxResult.Yes;
        });
        return Task.FromResult(result);
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private static MessageBoxImage ToImage(MessageIcon icon) => icon switch
    {
        MessageIcon.Warning => MessageBoxImage.Warning,
        MessageIcon.Error => MessageBoxImage.Error,
        MessageIcon.Question => MessageBoxImage.Question,
        MessageIcon.Information => MessageBoxImage.Information,
        _ => MessageBoxImage.None
    };
}
