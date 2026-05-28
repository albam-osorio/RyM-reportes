using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using RymReportes.Web.Options;

namespace RymReportes.Web.Services.Auth;

public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendPasswordResetAsync(
        string to,
        string resetUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Password))
        {
            logger.LogWarning("SMTP password is not configured. Password reset email was not sent to {Email}.", to);
            throw new InvalidOperationException("El envio de correo no esta configurado.");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = "Restablecer contraseña - RYM Reportes";

        var builder = new BodyBuilder
        {
            TextBody = $"""
                Se solicitó restablecer la contraseña de tu cuenta en RYM Reportes.

                Abre este enlace para crear una nueva contraseña:
                {resetUrl}

                Si no solicitaste este cambio, puedes ignorar este correo.
                """
        };

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOptions, cancellationToken);
        await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
