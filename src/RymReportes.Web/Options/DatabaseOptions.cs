using System.ComponentModel.DataAnnotations;

namespace RymReportes.Web.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
