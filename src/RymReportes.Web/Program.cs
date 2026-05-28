using System.Net;
using System.Security.Claims;
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
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
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
    options.LoginPath = "/login.html";
    options.AccessDeniedPath = "/login.html";
    options.Cookie.Name = "RymReportes.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
});

builder.Services.AddSingleton<ReportPeriodCalculator>();
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

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
        && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login.html");
        return;
    }

    if (context.Request.Path.Equals("/admin/users.html", StringComparison.OrdinalIgnoreCase))
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.Redirect("/login.html");
            return;
        }

        if (!context.User.IsInRole(AppRoles.Admin))
        {
            context.Response.Redirect("/");
            return;
        }
    }

    await next();
});
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
            context.Response.Redirect("/force-password-change.html");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ProblemDetailsResponse("Debe cambiar su contrasena antes de continuar."));
        return;
    }

    await next();
});
app.UseStaticFiles();
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
        return Results.BadRequest(new ProblemDetailsResponse("Email, nombre y contrasena son obligatorios."));
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
        return Results.BadRequest(new ProblemDetailsResponse("Email y contrasena son obligatorios."));
    }

    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Email o contrasena invalidos."));
    }

    var passwordResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (!passwordResult.Succeeded)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Email o contrasena invalidos."));
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
    UserManager<ApplicationUser> userManager) =>
{
    var user = await userManager.GetUserAsync(principal);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var roles = await userManager.GetRolesAsync(user);
    return Results.Ok(new
    {
        user.Email,
        user.FullName,
        user.MustChangePassword,
        roles
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
    var resetUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/reset-password.html?email={WebUtility.UrlEncode(user.Email)}&token={WebUtility.UrlEncode(token)}";
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
        return Results.BadRequest(new ProblemDetailsResponse("Contrasena actual y nueva son obligatorias."));
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
    UserManager<ApplicationUser> userManager) =>
{
    var users = await userManager.Users
        .OrderBy(user => user.Email)
        .Select(user => new
        {
            user.Id,
            user.Email,
            user.FullName,
            user.IsApproved,
            user.IsActive,
            user.MustChangePassword
        })
        .ToListAsync();

    return Results.Ok(users);
}).RequireAuthorization("AdminOnly");

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
}).RequireAuthorization("AdminOnly");

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
}).RequireAuthorization("AdminOnly");

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
}).RequireAuthorization();

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
}).RequireAuthorization();

app.Run();

static bool IsHtmlRequest(HttpRequest request) =>
    request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

static bool IsPasswordChangeAllowedPath(PathString path) =>
    path.StartsWithSegments("/auth/change-password")
    || path.StartsWithSegments("/auth/logout")
    || path.StartsWithSegments("/auth/me")
    || path.StartsWithSegments("/force-password-change.html")
    || path.StartsWithSegments("/login.html")
    || path.StartsWithSegments("/styles.css")
    || path.StartsWithSegments("/auth.js")
    || path.StartsWithSegments("/assets")
    || path.StartsWithSegments("/health");

public sealed record OrdersReportRequest(IReadOnlyList<string> OrderNumbers);

public sealed record ProblemDetailsResponse(string Detail);

public sealed record RegisterRequest(string Email, string FullName, string Password);

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SetActiveRequest(bool IsActive);
