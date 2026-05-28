using System.ComponentModel.DataAnnotations;

namespace RymReportes.Web.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string SmtpHost { get; set; } = "smtp.gmail.com";

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = "rym.application@gmail.com";

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "rym.application@gmail.com";
}
