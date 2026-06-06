using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EmailAI.Infrastructure.Services.Mail;

internal static class BrowserLauncher
{
    public static void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.", nameof(url));

        Exception? last = null;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }
        catch (Exception ex) { last = ex; }

        try
        {
            // Windows fallback
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{url}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return;
        }
        catch (Exception ex) { last = ex; }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var psi = new ProcessStartInfo("rundll32.exe", $"url.dll,FileProtocolHandler {url}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return;
            }
            catch (Exception ex) { last = ex; }
        }

        throw new InvalidOperationException(
            $"Could not open your web browser automatically.\n\nCopy and paste this URL into Chrome/Edge:\n{url}",
            last);
    }
}
