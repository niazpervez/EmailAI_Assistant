using EmailAI.UI.Shared.Abstractions;

namespace EmailAI.MAUI.Services;

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => MainThread.IsMainThread;

    public void Invoke(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    public Task InvokeAsync(Func<Task> action)
    {
        if (MainThread.IsMainThread)
            return action();
        return MainThread.InvokeOnMainThreadAsync(action);
    }
}
