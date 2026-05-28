using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RymReportes.Web.Data;
using RymReportes.Web.Identity;

namespace RymReportes.Web.Services.Auth;

public sealed class IdentitySeedService(
    ApplicationDbContext dbContext,
    RoleManager<IdentityRole> roleManager)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        foreach (var role in new[] { AppRoles.Admin, AppRoles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }
}
