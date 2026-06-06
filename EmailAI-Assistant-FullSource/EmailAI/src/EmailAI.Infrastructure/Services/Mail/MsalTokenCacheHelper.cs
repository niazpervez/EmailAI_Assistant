using EmailAI.Core;
using Microsoft.Identity.Client;

namespace EmailAI.Infrastructure.Services.Mail;

/// <summary>Persists Microsoft MSAL tokens so Outlook/Hotmail stays signed in after restart.</summary>
internal static class MsalTokenCacheHelper
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppName,
        "msal-cache.bin");

    public static void Bind(IPublicClientApplication app)
    {
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (File.Exists(CachePath))
                args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(CachePath));
        });

        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllBytes(CachePath, args.TokenCache.SerializeMsalV3());
        });
    }

    public static void Clear()
    {
        if (File.Exists(CachePath))
            File.Delete(CachePath);
    }
}
