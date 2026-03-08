using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Data;
using backend.Middleware;
using backend.Services;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication — validate all JWT config at startup (11.3)
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException("Jwt:Key is not configured. Set it via user-secrets or environment variables.");
if (!builder.Environment.IsDevelopment() && jwtKey.Contains("DEV-ONLY", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The development JWT key must not be used in production. Set a secure Jwt:Key via environment variables.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
if (string.IsNullOrWhiteSpace(jwtIssuer))
    throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"];
if (string.IsNullOrWhiteSpace(jwtAudience))
    throw new InvalidOperationException("Jwt:Audience is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// In-memory cache — used by IsActiveMiddleware (Fix #8) and AdminService cache invalidation
builder.Services.AddMemoryCache();

// #10: Needed by AppDbContext.SaveChangesAsync to resolve the current user for audit logging
builder.Services.AddHttpContextAccessor();

// Rate limiter (2.5)
builder.Services.AddSingleton<LoginRateLimiter>();

// Services
builder.Services.AddHttpClient<ITimeService, WorldTimeApiService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IAdminService, AdminService>();

// Offline queue failsafe — persists clock events to JSON when DB is unreachable
builder.Services.AddSingleton<OfflineQueueService>();
builder.Services.AddHostedService<OfflineSyncService>();

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS — read allowed origins from config, fallback to localhost (11.1)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Middleware pipeline
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoginRateLimitMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Fix #9: Enforce HTTPS and HSTS in production
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseCors("AllowFrontend");

// Cache-Control: prevent browser back button showing stale authenticated pages (12.3)
app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    await next();
});

app.UseAuthentication();
app.UseMiddleware<IsActiveMiddleware>();
app.UseAuthorization();
app.MapControllers();

// Auto-migrate and seed admin user on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // InMemory provider (used in tests) does not support Migrate(); fall back to EnsureCreated.
    if (db.Database.IsRelational())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();

    // Read seed credentials before the check so the condition uses the actual username
    var seedUsername = builder.Configuration["Seed:AdminUsername"] ?? "admin";
    var seedPassword = builder.Configuration["Seed:AdminPassword"] ?? "admin123";

    if (!await db.Users.AnyAsync(u => u.Username == seedUsername))
    {
        db.Users.Add(new backend.Models.User
        {
            Username = seedUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword),
            FullName = "System Administrator",
            Role = "Admin"
        });
        await db.SaveChangesAsync();

        if (seedPassword == "admin123")
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("Admin seeded with default credentials. Set Seed:AdminUsername and Seed:AdminPassword environment variables for production.");
        }
    }
}

app.Run();

// Expose Program to the test project (WebApplicationFactory<Program> requirement).
public partial class Program { }
