using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RymReportes.Web.Data;
using RymReportes.Web.Identity;
using RymReportes.Web.Models;
using RymReportes.Web.Options;
using RymReportes.Web.Services;
using RymReportes.Web.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "RYM Reportes Natura";
});

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations();
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var databaseOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
    options.UseSqlServer(databaseOptions.ConnectionString);
});

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.Cookie.Name = "RymReportes.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
    options.AddPolicy(AppPermissions.UsersManage, policy =>
        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.UsersManage));
    options.AddPolicy(AppPermissions.ReportsEventsAccess, policy =>
        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ReportsEventsAccess));
    options.AddPolicy(AppPermissions.ReportsEventsDownload, policy =>
        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.ReportsEventsDownload));
    options.AddPolicy(AppPermissions.PreferencesManageOwn, policy =>
        policy.RequireClaim(AppPermissions.ClaimType, AppPermissions.PreferencesManageOwn));
});

builder.Services.AddSingleton<OrderNumberNormalizer>();
builder.Services.AddSingleton<IReportExcelGenerator, ClosedXmlReportExcelGenerator>();
builder.Services.AddScoped<IEventReportRepository, SqlEventReportRepository>();
builder.Services.AddScoped<IReportFileService, ReportFileService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IdentitySeedService>();
builder.Services.AddScoped<AdminCommandRunner>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    if (args.Length > 0 && string.Equals(args[0], "admin", StringComparison.OrdinalIgnoreCase))
    {
        var runner = scope.ServiceProvider.GetRequiredService<AdminCommandRunner>();
        Environment.ExitCode = await runner.RunAsync(args, CancellationToken.None);
        return;
    }

    await scope.ServiceProvider.GetRequiredService<IdentitySeedService>().InitializeAsync(CancellationToken.None);
}

app.UseStaticFiles();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated != true || IsPasswordChangeAllowedPath(context.Request.Path))
    {
        await next();
        return;
    }

    var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
    var user = await userManager.GetUserAsync(context.User);
    if (user is not null && user.MustChangePassword)
    {
        if (IsHtmlRequest(context.Request))
        {
            context.Response.Redirect("/force-password-change");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ProblemDetailsResponse("Debe cambiar su contraseña antes de continuar."));
        return;
    }

    await next();
});
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/", (IWebHostEnvironment environment) =>
{
    var path = Path.Combine(environment.WebRootPath, "index.html");
    return Results.File(path, "text/html; charset=utf-8");
}).RequireAuthorization();

app.MapPost("/auth/register", async (
    RegisterRequest request,
    UserManager<ApplicationUser> userManager) =>
{
    if (string.IsNullOrWhiteSpace(request.Email)
        || string.IsNullOrWhiteSpace(request.FullName)
        || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Email, nombre y contraseña son obligatorios."));
    }

    var email = request.Email.Trim();
    var existingUser = await userManager.FindByEmailAsync(email);
    if (existingUser is not null)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Ya existe un usuario con ese email."));
    }

    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        FullName = request.FullName.Trim(),
        IsApproved = false,
        IsActive = true,
        EmailConfirmed = true
    };

    var result = await userManager.CreateAsync(user, request.Password);
    return result.Succeeded
        ? Results.Ok(new { status = "pending" })
        : Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
});

app.MapPost("/auth/login", async (
    LoginRequest request,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Email y contraseña son obligatorios."));
    }

    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Email o contraseña inválidos."));
    }

    var passwordResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (!passwordResult.Succeeded)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Email o contraseña inválidos."));
    }

    if (!user.IsApproved)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (!user.IsActive)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    await signInManager.SignInAsync(user, isPersistent: request.RememberMe);
    return Results.Ok(new { mustChangePassword = user.MustChangePassword });
});

app.MapPost("/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/auth/me", async (
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager) =>
{
    var user = await userManager.GetUserAsync(principal);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var roles = await userManager.GetRolesAsync(user);
    var permissions = await GetUserPermissionsAsync(user, userManager, roleManager);
    return Results.Ok(new
    {
        user.Email,
        user.FullName,
        user.MustChangePassword,
        roles,
        permissions
    });
}).RequireAuthorization();

app.MapPost("/auth/forgot-password", async (
    ForgotPasswordRequest request,
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.Ok();
    }

    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null || !user.IsActive || !user.IsApproved)
    {
        return Results.Ok();
    }

    var token = await userManager.GeneratePasswordResetTokenAsync(user);
    var resetUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/reset-password?email={WebUtility.UrlEncode(user.Email)}&token={WebUtility.UrlEncode(token)}";
    try
    {
        await emailSender.SendPasswordResetAsync(user.Email!, resetUrl, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ProblemDetailsResponse(ex.Message));
    }

    return Results.Ok();
});

app.MapPost("/auth/reset-password", async (
    ResetPasswordRequest request,
    UserManager<ApplicationUser> userManager) =>
{
    if (string.IsNullOrWhiteSpace(request.Email)
        || string.IsNullOrWhiteSpace(request.Token)
        || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Solicitud invalida."));
    }

    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Solicitud invalida."));
    }

    var result = await userManager.ResetPasswordAsync(user, request.Token, request.Password);
    if (!result.Succeeded)
    {
        return Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
    }

    user.MustChangePassword = false;
    await userManager.UpdateAsync(user);
    return Results.Ok();
});

app.MapPost("/auth/change-password", async (
    ChangePasswordRequest request,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) =>
{
    if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Contraseña actual y nueva son obligatorias."));
    }

    var user = await userManager.GetUserAsync(principal);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    if (!result.Succeeded)
    {
        return Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
    }

    user.MustChangePassword = false;
    await userManager.UpdateAsync(user);
    await signInManager.RefreshSignInAsync(user);
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/admin/users", async (
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager) =>
{
    var users = await userManager.Users
        .OrderBy(user => user.Email)
        .ToListAsync();

    var result = new List<object>(users.Count);
    foreach (var user in users)
    {
        var roles = await GetUserRolesAsync(user, userManager, roleManager);
        result.Add(new
        {
            user.Id,
            user.Email,
            user.FullName,
            user.IsApproved,
            user.IsActive,
            user.MustChangePassword,
            user.ConcurrencyStamp,
            roles
        });
    }

    return Results.Ok(result);
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapGet("/admin/roles", async (
    RoleManager<ApplicationRole> roleManager) =>
{
    var roles = await roleManager.Roles
        .OrderBy(role => role.DisplayOrder)
        .ThenBy(role => role.DisplayName)
        .ThenBy(role => role.Name)
        .ToListAsync();

    var result = new List<object>(roles.Count);
    foreach (var role in roles)
    {
        var permissions = await GetRolePermissionsAsync(role, roleManager);
        result.Add(new
        {
            role.Id,
            role.Name,
            DisplayName = string.IsNullOrWhiteSpace(role.DisplayName) ? role.Name : role.DisplayName,
            role.DisplayOrder,
            role.ConcurrencyStamp,
            Permissions = permissions
        });
    }

    return Results.Ok(result);
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapGet("/admin/permissions", () => Results.Ok(AllPermissions()))
    .RequireAuthorization(AppPermissions.UsersManage);

app.MapPost("/admin/roles", async (
    ManagedRoleRequest request,
    RoleManager<ApplicationRole> roleManager) =>
{
    var validationError = await ValidateRoleRequestAsync(request, roleManager);
    if (validationError is not null)
    {
        return validationError;
    }

    var permissions = NormalizePermissions(request.Permissions);
    var role = new ApplicationRole
    {
        Name = request.Name.Trim(),
        DisplayName = request.DisplayName.Trim(),
        DisplayOrder = request.DisplayOrder
    };

    var createResult = await roleManager.CreateAsync(role);
    if (!createResult.Succeeded)
    {
        return Results.BadRequest(new { errors = createResult.Errors.Select(error => error.Description) });
    }

    await UpdateRolePermissionsAsync(role, permissions, roleManager);
    return Results.Ok(await ToRoleResponseAsync(role, roleManager));
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPut("/admin/roles/{id}", async (
    string id,
    ManagedRoleRequest request,
    RoleManager<ApplicationRole> roleManager) =>
{
    var role = await roleManager.FindByIdAsync(id);
    if (role is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(role.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
    {
        return Results.Conflict(new ProblemDetailsResponse("El rol fue modificado por otro proceso. Revisa los datos e intenta de nuevo."));
    }

    var validationError = await ValidateRoleRequestAsync(request, roleManager, id);
    if (validationError is not null)
    {
        return validationError;
    }

    if (role.Name is AppRoles.Admin or AppRoles.User
        && !string.Equals(role.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ProblemDetailsResponse("No se puede cambiar el nombre tecnico de los roles base."));
    }

    role.Name = request.Name.Trim();
    role.NormalizedName = roleManager.NormalizeKey(role.Name);
    role.DisplayName = request.DisplayName.Trim();
    role.DisplayOrder = request.DisplayOrder;

    var updateResult = await roleManager.UpdateAsync(role);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { errors = updateResult.Errors.Select(error => error.Description) });
    }

    await UpdateRolePermissionsAsync(role, NormalizePermissions(request.Permissions), roleManager);
    return Results.Ok(await ToRoleResponseAsync(role, roleManager));
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapDelete("/admin/roles/{id}", async (
    string id,
    ApplicationDbContext dbContext,
    RoleManager<ApplicationRole> roleManager) =>
{
    var role = await roleManager.FindByIdAsync(id);
    if (role is null)
    {
        return Results.NotFound();
    }

    if (role.Name is AppRoles.Admin or AppRoles.User)
    {
        return Results.BadRequest(new ProblemDetailsResponse("No se pueden eliminar los roles base."));
    }

    var usersWithRole = await dbContext.UserRoles.CountAsync(userRole => userRole.RoleId == role.Id);
    if (usersWithRole > 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse("No se puede eliminar un rol asignado a usuarios. Retira primero el rol de esos usuarios."));
    }

    var deleteResult = await roleManager.DeleteAsync(role);
    return deleteResult.Succeeded
        ? Results.Ok()
        : Results.BadRequest(new { errors = deleteResult.Errors.Select(error => error.Description) });
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPost("/admin/users", async (
    ManagedUserCreateRequest request,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager) =>
{
    var email = request.Email.Trim();
    var fullName = request.FullName.Trim();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Nombre y email son obligatorios."));
    }

    var existingUser = await userManager.FindByEmailAsync(email);
    if (existingUser is not null)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Ya existe un usuario con ese email."));
    }

    var requestedRoles = request.RoleNames
        .Select(role => role.Trim())
        .Where(role => !string.IsNullOrWhiteSpace(role))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var knownRoles = await roleManager.Roles.Select(role => role.Name!).ToListAsync();
    var unknownRoles = requestedRoles
        .Except(knownRoles, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (unknownRoles.Length > 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse($"Roles invalidos: {string.Join(", ", unknownRoles)}."));
    }

    var temporaryPassword = GenerateTemporaryPassword();
    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        EmailConfirmed = true,
        FullName = fullName,
        IsApproved = request.IsApproved,
        IsActive = request.IsActive,
        MustChangePassword = true,
        ApprovedAt = request.IsApproved ? DateTimeOffset.UtcNow : null,
        ApprovedBy = request.IsApproved ? principal.Identity?.Name : null
    };

    var createResult = await userManager.CreateAsync(user, temporaryPassword);
    if (!createResult.Succeeded)
    {
        return Results.BadRequest(new { errors = createResult.Errors.Select(error => error.Description) });
    }

    if (requestedRoles.Length > 0)
    {
        var addResult = await userManager.AddToRolesAsync(user, requestedRoles);
        if (!addResult.Succeeded)
        {
            return Results.BadRequest(new { errors = addResult.Errors.Select(error => error.Description) });
        }
    }

    return Results.Ok(new { user.Id, temporaryPassword });
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPut("/admin/users/{id}", async (
    string id,
    ManagedUserUpdateRequest request,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user is null)
    {
        return Results.NotFound();
    }

    if (!string.Equals(user.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
    {
        return Results.Conflict(new ProblemDetailsResponse("El usuario fue modificado por otro proceso. Revisa los datos e intenta de nuevo."));
    }

    var email = request.Email.Trim();
    var fullName = request.FullName.Trim();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Nombre y email son obligatorios."));
    }

    var existingUser = await userManager.FindByEmailAsync(email);
    if (existingUser is not null && existingUser.Id != user.Id)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Ya existe otro usuario con ese email."));
    }

    var requestedRoles = request.RoleNames
        .Select(role => role.Trim())
        .Where(role => !string.IsNullOrWhiteSpace(role))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var knownRoles = await roleManager.Roles.Select(role => role.Name!).ToListAsync();
    var unknownRoles = requestedRoles
        .Except(knownRoles, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (unknownRoles.Length > 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse($"Roles invalidos: {string.Join(", ", unknownRoles)}."));
    }

    var currentUserId = userManager.GetUserId(principal);
    if (user.Id == currentUserId)
    {
        if (!request.IsActive || !request.IsApproved)
        {
            return Results.BadRequest(new ProblemDetailsResponse("No puedes quitarte tu propio acceso."));
        }

        if (!requestedRoles.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new ProblemDetailsResponse("No puedes quitarte el rol administrador."));
        }
    }

    user.FullName = fullName;
    user.Email = email;
    user.UserName = email;
    user.NormalizedEmail = userManager.NormalizeEmail(email);
    user.NormalizedUserName = userManager.NormalizeName(email);
    user.IsApproved = request.IsApproved;
    user.IsActive = request.IsActive;

    if (request.IsApproved && user.ApprovedAt is null)
    {
        user.ApprovedAt = DateTimeOffset.UtcNow;
        user.ApprovedBy = principal.Identity?.Name;
    }

    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { errors = updateResult.Errors.Select(error => error.Description) });
    }

    var currentRoles = await userManager.GetRolesAsync(user);
    var rolesToRemove = currentRoles.Except(requestedRoles, StringComparer.OrdinalIgnoreCase).ToArray();
    var rolesToAdd = requestedRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();

    if (rolesToRemove.Length > 0)
    {
        var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
        if (!removeResult.Succeeded)
        {
            return Results.BadRequest(new { errors = removeResult.Errors.Select(error => error.Description) });
        }
    }

    if (rolesToAdd.Length > 0)
    {
        var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
        if (!addResult.Succeeded)
        {
            return Results.BadRequest(new { errors = addResult.Errors.Select(error => error.Description) });
        }
    }

    var roles = await GetUserRolesAsync(user, userManager, roleManager);
    return Results.Ok(new
    {
        user.Id,
        user.Email,
        user.FullName,
        user.IsApproved,
        user.IsActive,
        user.MustChangePassword,
        user.ConcurrencyStamp,
        roles
    });
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPost("/admin/users/{id}/deactivate", async (
    string id,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByIdAsync(id);
    var currentUserId = userManager.GetUserId(principal);
    if (user is null)
    {
        return Results.NotFound();
    }

    if (user.Id == currentUserId)
    {
        return Results.BadRequest(new ProblemDetailsResponse("No puedes desactivar tu propio usuario."));
    }

    user.IsActive = false;
    var result = await userManager.UpdateAsync(user);
    return result.Succeeded
        ? Results.Ok()
        : Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPost("/admin/users/{id}/approve", async (
    string id,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user is null)
    {
        return Results.NotFound();
    }

    user.IsApproved = true;
    user.IsActive = true;
    user.ApprovedAt = DateTimeOffset.UtcNow;
    user.ApprovedBy = principal.Identity?.Name;
    await userManager.UpdateAsync(user);

    if (!await userManager.IsInRoleAsync(user, AppRoles.User))
    {
        await userManager.AddToRoleAsync(user, AppRoles.User);
    }

    return Results.Ok();
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPost("/admin/users/{id}/active", async (
    string id,
    SetActiveRequest request,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByIdAsync(id);
    var currentUserId = userManager.GetUserId(principal);
    if (user is null)
    {
        return Results.NotFound();
    }

    if (user.Id == currentUserId && !request.IsActive)
    {
        return Results.BadRequest(new ProblemDetailsResponse("No puedes desactivar tu propio usuario."));
    }

    user.IsActive = request.IsActive;
    await userManager.UpdateAsync(user);
    return Results.Ok();
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapPost("/admin/users/{id}/role", async (
    string id,
    SetRoleRequest request,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager) =>
{
    if (request.Role is not (AppRoles.Admin or AppRoles.User))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Rol invalido."));
    }

    var user = await userManager.FindByIdAsync(id);
    var currentUserId = userManager.GetUserId(principal);
    if (user is null)
    {
        return Results.NotFound();
    }

    if (user.Id == currentUserId && request.Role != AppRoles.Admin)
    {
        return Results.BadRequest(new ProblemDetailsResponse("No puedes quitarte el rol administrador."));
    }

    var currentRoles = await userManager.GetRolesAsync(user);
    var rolesToRemove = currentRoles.Where(role => role is AppRoles.Admin or AppRoles.User).ToArray();
    if (rolesToRemove.Length > 0)
    {
        var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
        if (!removeResult.Succeeded)
        {
            return Results.BadRequest(new { errors = removeResult.Errors.Select(error => error.Description) });
        }
    }

    var addResult = await userManager.AddToRoleAsync(user, request.Role);
    if (!addResult.Succeeded)
    {
        return Results.BadRequest(new { errors = addResult.Errors.Select(error => error.Description) });
    }

    return Results.Ok();
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapDelete("/admin/users/{id}", async (
    string id,
    ClaimsPrincipal principal,
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.FindByIdAsync(id);
    var currentUserId = userManager.GetUserId(principal);
    if (user is null)
    {
        return Results.NotFound();
    }

    if (user.Id == currentUserId)
    {
        return Results.BadRequest(new ProblemDetailsResponse("No puedes desactivar tu propio usuario."));
    }

    user.IsActive = false;
    var result = await userManager.UpdateAsync(user);
    return result.Succeeded
        ? Results.Ok()
        : Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });
}).RequireAuthorization(AppPermissions.UsersManage);

app.MapGet("/reportes/mensual/download", async (
    string? month,
    IReportFileService reportFileService,
    CancellationToken cancellationToken) =>
{
    if (!YearMonth.TryParse(month, out var yearMonth))
    {
        return Results.BadRequest(new ProblemDetailsResponse("El parametro month debe tener formato YYYY-MM."));
    }

    var report = await reportFileService.CreateMonthlyReportAsync(yearMonth, cancellationToken);
    return Results.File(
        report.Content,
        report.ContentType,
        report.FileName);
}).RequireAuthorization(AppPermissions.ReportsEventsDownload);

app.MapPost("/reportes/pedidos/download", async (
    OrdersReportRequest request,
    IReportFileService reportFileService,
    CancellationToken cancellationToken) =>
{
    if (request.OrderNumbers is null || request.OrderNumbers.Count == 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Debe enviar al menos un numero de pedido."));
    }

    try
    {
        var report = await reportFileService.CreateOrdersReportAsync(request.OrderNumbers, cancellationToken);
        return Results.File(
            report.Content,
            report.ContentType,
            report.FileName);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ProblemDetailsResponse(ex.Message));
    }
}).RequireAuthorization(AppPermissions.ReportsEventsDownload);

app.MapFallbackToFile("index.html");

app.Run();

static bool IsHtmlRequest(HttpRequest request) =>
    request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

static bool IsApiRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/auth")
    || request.Path.StartsWithSegments("/admin")
    || request.Path.StartsWithSegments("/reportes")
    || !IsHtmlRequest(request);

static bool IsPasswordChangeAllowedPath(PathString path) =>
    path.StartsWithSegments("/auth/change-password")
    || path.StartsWithSegments("/auth/logout")
    || path.StartsWithSegments("/auth/me")
    || path.StartsWithSegments("/force-password-change")
    || path.StartsWithSegments("/login")
    || path.StartsWithSegments("/register")
    || path.StartsWithSegments("/forgot-password")
    || path.StartsWithSegments("/reset-password")
    || path.StartsWithSegments("/health");

static string GenerateTemporaryPassword()
{
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    Span<char> randomPart = stackalloc char[14];
    for (var i = 0; i < randomPart.Length; i++)
    {
        randomPart[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }

    return $"Rym!{new string(randomPart)}9aZ";
}

static IReadOnlyList<string> AllPermissions() =>
[
    AppPermissions.PlatformHomeAccess,
    AppPermissions.UsersManage,
    AppPermissions.ReportsEventsAccess,
    AppPermissions.ReportsEventsDownload,
    AppPermissions.PreferencesManageOwn
];

static IReadOnlyList<string> NormalizePermissions(IEnumerable<string>? permissions) =>
    (permissions ?? [])
        .Select(permission => permission.Trim())
        .Where(permission => !string.IsNullOrWhiteSpace(permission))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

static async Task<IResult?> ValidateRoleRequestAsync(
    ManagedRoleRequest request,
    RoleManager<ApplicationRole> roleManager,
    string? currentRoleId = null)
{
    var name = request.Name.Trim();
    var displayName = request.DisplayName.Trim();
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(displayName))
    {
        return Results.BadRequest(new ProblemDetailsResponse("Nombre tecnico y nombre visible son obligatorios."));
    }

    if (!name.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-'))
    {
        return Results.BadRequest(new ProblemDetailsResponse("El nombre tecnico solo puede contener letras, numeros, punto, guion o guion bajo."));
    }

    var existingRole = await roleManager.FindByNameAsync(name);
    if (existingRole is not null && existingRole.Id != currentRoleId)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Ya existe un rol con ese nombre tecnico."));
    }

    var knownPermissions = AllPermissions().ToHashSet(StringComparer.OrdinalIgnoreCase);
    var unknownPermissions = NormalizePermissions(request.Permissions)
        .Where(permission => !knownPermissions.Contains(permission))
        .ToArray();
    if (unknownPermissions.Length > 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse($"Permisos invalidos: {string.Join(", ", unknownPermissions)}."));
    }

    return null;
}

static async Task UpdateRolePermissionsAsync(
    ApplicationRole role,
    IReadOnlyList<string> permissions,
    RoleManager<ApplicationRole> roleManager)
{
    var currentClaims = await roleManager.GetClaimsAsync(role);
    var currentPermissions = currentClaims
        .Where(claim => claim.Type == AppPermissions.ClaimType)
        .ToArray();
    var requestedPermissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var claim in currentPermissions.Where(claim => !requestedPermissions.Contains(claim.Value)))
    {
        await roleManager.RemoveClaimAsync(role, claim);
    }

    var existingPermissions = currentPermissions
        .Select(claim => claim.Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var permission in permissions.Where(permission => !existingPermissions.Contains(permission)))
    {
        await roleManager.AddClaimAsync(role, new Claim(AppPermissions.ClaimType, permission));
    }
}

static async Task<object> ToRoleResponseAsync(
    ApplicationRole role,
    RoleManager<ApplicationRole> roleManager)
{
    var permissions = await GetRolePermissionsAsync(role, roleManager);
    return new
    {
        role.Id,
        role.Name,
        DisplayName = string.IsNullOrWhiteSpace(role.DisplayName) ? role.Name : role.DisplayName,
        role.DisplayOrder,
        role.ConcurrencyStamp,
        Permissions = permissions
    };
}

static async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
    ApplicationUser user,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager)
{
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var claim in await userManager.GetClaimsAsync(user))
    {
        if (claim.Type == AppPermissions.ClaimType)
        {
            permissions.Add(claim.Value);
        }
    }

    foreach (var roleName in await userManager.GetRolesAsync(user))
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            continue;
        }

        foreach (var claim in await roleManager.GetClaimsAsync(role))
        {
            if (claim.Type == AppPermissions.ClaimType)
            {
                permissions.Add(claim.Value);
            }
        }
    }

    return permissions.Order(StringComparer.OrdinalIgnoreCase).ToArray();
}

static async Task<IReadOnlyList<string>> GetRolePermissionsAsync(
    ApplicationRole role,
    RoleManager<ApplicationRole> roleManager)
{
    return (await roleManager.GetClaimsAsync(role))
        .Where(claim => claim.Type == AppPermissions.ClaimType)
        .Select(claim => claim.Value)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static async Task<IReadOnlyList<object>> GetUserRolesAsync(
    ApplicationUser user,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager)
{
    var roleNames = await userManager.GetRolesAsync(user);
    var roles = await roleManager.Roles
        .Where(role => role.Name != null && roleNames.Contains(role.Name))
        .OrderBy(role => role.DisplayOrder)
        .ThenBy(role => role.DisplayName)
        .ThenBy(role => role.Name)
        .Select(role => new
        {
            role.Id,
            role.Name,
            DisplayName = string.IsNullOrWhiteSpace(role.DisplayName) ? role.Name : role.DisplayName,
            role.DisplayOrder
        })
        .Cast<object>()
        .ToListAsync();

    return roles;
}

public sealed record OrdersReportRequest(IReadOnlyList<string> OrderNumbers);

public sealed record ProblemDetailsResponse(string Detail);

public sealed record RegisterRequest(string Email, string FullName, string Password);

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SetActiveRequest(bool IsActive);

public sealed record SetRoleRequest(string Role);

public sealed record ManagedRoleRequest(
    string Name,
    string DisplayName,
    int DisplayOrder,
    string ConcurrencyStamp,
    IReadOnlyList<string> Permissions);

public sealed record ManagedUserCreateRequest(
    string Email,
    string FullName,
    bool IsApproved,
    bool IsActive,
    IReadOnlyList<string> RoleNames);

public sealed record ManagedUserUpdateRequest(
    string Email,
    string FullName,
    bool IsApproved,
    bool IsActive,
    string ConcurrencyStamp,
    IReadOnlyList<string> RoleNames);
