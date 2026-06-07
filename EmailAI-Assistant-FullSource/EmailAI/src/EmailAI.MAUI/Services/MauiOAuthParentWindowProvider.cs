using EmailAI.Core.Interfaces;

namespace EmailAI.MAUI.Services;

public sealed class MauiOAuthParentWindowProvider : IOAuthParentWindowProvider
{
    public nint GetOwnerWindowHandle() => 0;
}
