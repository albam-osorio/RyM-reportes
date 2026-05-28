using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using RymReportes.Web.Data;
using RymReportes.Web.Identity;

namespace RymReportes.Web.Services.Auth;

public sealed class IdentitySeedService(
    ApplicationDbContext dbContext,
    RoleManager<ApplicationRole> roleManager)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        await EnsureRoleAsync(AppRoles.Admin, "Admin", 0);
        await EnsureRoleAsync(AppRoles.User, "User", 10);

        await EnsureRolePermissionsAsync(
            AppRoles.Admin,
            new[]
            {
                AppPermissions.PlatformHomeAccess,
                AppPermissions.UsersManage,
                AppPermissions.ReportsEventsAccess,
                AppPermissions.ReportsEventsDownload,
                AppPermissions.PreferencesManageOwn
            });

        await EnsureRolePermissionsAsync(
            AppRoles.User,
            new[]
            {
                AppPermissions.PlatformHomeAccess,
                AppPermissions.ReportsEventsAccess,
                AppPermissions.ReportsEventsDownload,
                AppPermissions.PreferencesManageOwn
            });
    }

    private async Task EnsureRoleAsync(string roleName, string displayName, int displayOrder)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            await roleManager.CreateAsync(new ApplicationRole
            {
                Name = roleName,
                DisplayName = displayName,
                DisplayOrder = displayOrder
            });
            return;
        }

        var changed = false;
        if (string.IsNullOrWhiteSpace(role.DisplayName))
        {
            role.DisplayName = displayName;
            changed = true;
        }

        if (role.DisplayOrder != displayOrder)
        {
            role.DisplayOrder = displayOrder;
            changed = true;
        }

        if (changed)
        {
            await roleManager.UpdateAsync(role);
        }
    }

    private async Task EnsureRolePermissionsAsync(string roleName, IEnumerable<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return;
        }

        var currentClaims = await roleManager.GetClaimsAsync(role);
        var currentPermissions = currentClaims
            .Where(claim => claim.Type == AppPermissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in permissions)
        {
            if (!currentPermissions.Contains(permission))
            {
                await roleManager.AddClaimAsync(role, new Claim(AppPermissions.ClaimType, permission));
            }
        }
    }
}
