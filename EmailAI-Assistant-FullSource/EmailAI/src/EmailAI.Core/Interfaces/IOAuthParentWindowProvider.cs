namespace EmailAI.Core.Interfaces;

/// <summary>Supplies the native window handle for OAuth browser prompts (WPF).</summary>
public interface IOAuthParentWindowProvider
{
    nint GetOwnerWindowHandle();
}
