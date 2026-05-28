using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using RymReportes.Web.Data;
using RymReportes.Web.Identity;

namespace RymReportes.Web.Services.Auth;

public sealed class AdminCommandRunner(
    UserManager<ApplicationUser> userManager,
    IdentitySeedService identitySeedService,
    ILogger<AdminCommandRunner> logger)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2 || !string.Equals(args[0], "admin", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        await identitySeedService.InitializeAsync(cancellationToken);

        return args[1].ToLowerInvariant() switch
        {
            "create" => await CreateAdminAsync(args),
            "reset-password" => await ResetPasswordAsync(args),
            _ => ShowUsage()
        };
    }

    private async Task<int> CreateAdminAsync(string[] args)
    {
        var email = ReadOption(args, "--email");
        var fullName = ReadOption(args, "--name") ?? email;
        var password = ReadOption(args, "--password") ?? GenerateTemporaryPassword();

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogError("Debe indicar --email.");
            return 2;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName ?? email,
                IsApproved = true,
                IsActive = true,
                MustChangePassword = true,
                ApprovedAt = DateTimeOffset.UtcNow,
                ApprovedBy = "infrastructure"
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                LogErrors(createResult);
                return 3;
            }
        }

        if (!await userManager.IsInRoleAsync(user, AppRoles.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Admin);
            if (!roleResult.Succeeded)
            {
                LogErrors(roleResult);
                return 4;
            }
        }

        logger.LogInformation("Admin listo: {Email}", email);
        logger.LogInformation("Contraseña temporal: {Password}", password);
        return 0;
    }

    private async Task<int> ResetPasswordAsync(string[] args)
    {
        var email = ReadOption(args, "--email");
        var password = ReadOption(args, "--password") ?? GenerateTemporaryPassword();

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogError("Debe indicar --email.");
            return 2;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            logger.LogError("No existe usuario con email {Email}.", email);
            return 3;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            LogErrors(result);
            return 4;
        }

        user.IsActive = true;
        user.IsApproved = true;
        user.MustChangePassword = true;
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);

        logger.LogInformation("Contraseña temporal para {Email}: {Password}", email, password);
        return 0;
    }

    private int ShowUsage()
    {
        logger.LogInformation("Uso:");
        logger.LogInformation("  admin create --email admin@empresa.com --name \"Nombre\" [--password Temporal123!]");
        logger.LogInformation("  admin reset-password --email admin@empresa.com [--password Temporal123!]");
        return 2;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string GenerateTemporaryPassword()
    {
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        return $"Rym-{Convert.ToHexString(bytes)}!";
    }

    private void LogErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            logger.LogError("{Code}: {Description}", error.Code, error.Description);
        }
    }
}
