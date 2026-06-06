using EmailAI.Core.Interfaces;
using System.Windows;
using System.Windows.Interop;

namespace EmailAI.WPF.Services;

public sealed class WpfOAuthParentWindowProvider : IOAuthParentWindowProvider
{
    public nint GetOwnerWindowHandle()
    {
        var window = System.Windows.Application.Current?.MainWindow;
        if (window is null || !window.IsLoaded)
            return 0;

        var helper = new WindowInteropHelper(window);
        return helper.Handle;
    }
}
