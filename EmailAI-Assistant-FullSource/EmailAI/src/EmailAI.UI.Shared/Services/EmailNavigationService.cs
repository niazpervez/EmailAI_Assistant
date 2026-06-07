namespace EmailAI.UI.Shared.Services;

public sealed class EmailNavigationService
{
    public event Action<string>? OpenEmailRequested;

    public void OpenEmail(string emailId)
    {
        if (string.IsNullOrWhiteSpace(emailId)) return;
        OpenEmailRequested?.Invoke(emailId);
    }
}
