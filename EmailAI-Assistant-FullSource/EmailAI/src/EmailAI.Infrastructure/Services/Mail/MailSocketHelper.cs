using EmailAI.Core.DTOs;
using MailKit.Security;

namespace EmailAI.Infrastructure.Services.Mail;

internal static class MailSocketHelper
{
    public static SecureSocketOptions GetImapOptions(MailAccountConfig account)
        => Resolve(account.ImapEncryption, account.ImapPort, defaultSsl: true);

    public static SecureSocketOptions GetSmtpOptions(MailAccountConfig account)
        => Resolve(account.SmtpEncryption, account.SmtpPort, defaultSsl: false);

    private static SecureSocketOptions Resolve(MailEncryptionMode mode, int port, bool defaultSsl)
    {
        if (mode != MailEncryptionMode.Auto)
        {
            return mode switch
            {
                MailEncryptionMode.SslTls   => SecureSocketOptions.SslOnConnect,
                MailEncryptionMode.StartTls => SecureSocketOptions.StartTls,
                MailEncryptionMode.None       => SecureSocketOptions.None,
                _                             => SecureSocketOptions.Auto
            };
        }

        // Automatic: common port conventions
        return port switch
        {
            993 or 465 => SecureSocketOptions.SslOnConnect,
            143 or 587 => SecureSocketOptions.StartTls,
            _ => defaultSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable
        };
    }
}
