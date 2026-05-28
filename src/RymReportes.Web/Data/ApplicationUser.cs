using Microsoft.AspNetCore.Identity;

namespace RymReportes.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public bool IsApproved { get; set; }

    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ApprovedBy { get; set; }
}
