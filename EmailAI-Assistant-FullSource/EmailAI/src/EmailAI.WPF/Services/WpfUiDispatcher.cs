using EmailAI.UI.Shared.Abstractions;
using System.Windows.Threading;

namespace EmailAI.WPF.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread =>
        System.Windows.Application.Current?.Dispatcher?.CheckAccess() ?? true;

    public void Invoke(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF application is not running.");

        if (dispatcher.CheckAccess())
            return action();

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task.Unwrap();
    }
}
