namespace EmailAI.UI.Shared.Abstractions;

public interface IUiDispatcher
{
    void Invoke(Action action);
    Task InvokeAsync(Func<Task> action);
    bool IsOnUiThread { get; }
}

public enum MessageIcon
{
    None,
    Information,
    Warning,
    Error,
    Question
}

public interface IMessageService
{
    Task ShowAlertAsync(string message, string title, MessageIcon icon = MessageIcon.Information);
    Task<bool> ShowConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question);
}

public interface IFilePickerService
{
    Task<string?> PickDatabaseFileAsync();
}

public interface INavigationViewFactory
{
    object? CreateView(string page, Action<object>? configure = null);
}
