namespace RymReportes.Web.Services.Auth;

public interface IEmailSender
{
    Task SendPasswordResetAsync(
        string to,
        string resetUrl,
        CancellationToken cancellationToken);
}
