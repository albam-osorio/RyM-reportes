using Microsoft.AspNetCore.Identity;

namespace RymReportes.Web.Data;

public sealed class ApplicationRole : IdentityRole
{
    public string DisplayName { get; set; } = string.Empty;

    public int DisplayOrder { get; set; } = 10;
}
