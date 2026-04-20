using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Pgvector;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Text.Json;
using UPACIP.Api.Configuration;
using UPACIP.Api.HealthChecks;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Api.Security;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;
using UPACIP.DataAccess.Seeding;
using UPACIP.Service.Validation;
using UPACIP.Service.VectorSearch;

var builder = WebApplication.CreateBuilder(args);

// Enable Windows Service lifecycle integration (start/stop/graceful shutdown signals).
// This is a no-op when running in console mode (dotnet run / development), so it does
// not affect the local developer workflow.
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "UPACIP.Api";
});

// Enforce TLS 1.2 and TLS 1.3 on all Kestrel HTTPS endpoints (AC-3, defense-in-depth).
// ASP.NET Core 8 defaults to OS-negotiated protocols; this explicit setting overrides
// the OS to guarantee TLS 1.0 and TLS 1.1 are never negotiated even if the SCHANNEL
// registry settings are not applied.
builder.WebHost.ConfigureKestrel(kestrelOptions =>
{
    kestrelOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols =
            System.Security.Authentication.SslProtocols.Tls12 |
            System.Security.Authentication.SslProtocols.Tls13;
    });
});

// ---------- Services ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// FluentValidation — auto-validates request DTOs before controller actions execute.
// Validators are discovered from the UPACIP.Service assembly via assembly scanning.
// When validation fails, the default ValidationProblemDetails (RFC 7807) response is
// replaced by our ErrorResponse model so all 400 responses share a consistent shape.
builder.Services
    .AddFluentValidationAutoValidation()
    .AddValidatorsFromAssemblyContaining<AppointmentDateValidator>();

// Override the default 400 response factory so FluentValidation errors use ErrorResponse
// (same shape as constraint/exception errors) instead of ValidationProblemDetails.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();

        var validationErrors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var errorResponse = new ErrorResponse
        {
            StatusCode       = (int)HttpStatusCode.BadRequest,
            Message          = "One or more validation errors occurred.",
            CorrelationId    = correlationId,
            Timestamp        = DateTimeOffset.UtcNow,
            ValidationErrors = validationErrors
        };

        return new BadRequestObjectResult(errorResponse);
    };
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "UPACIP API",
        Version = "v1",
        Description = "Unified Patient Access & Clinical Intelligence Platform – Backend API"
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:3000" })
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// EF Core — PostgreSQL via Npgsql (NFR-028: Maximum Pool Size=100 in connection string)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. " +
        "Set it via user secrets: dotnet user-secrets set ConnectionStrings:DefaultConnection \"<value>\"");

// Build an NpgsqlDataSource with pgvector type support registered (AC-AI-001).
// UseVector() must be called on the data source builder before any connections
// are established so that Npgsql's type mapper includes the Vector <-> vector(n) mapping.
var npgsqlDataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
npgsqlDataSourceBuilder.UseVector();
var npgsqlDataSource = npgsqlDataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        npgsqlDataSource,
        npgsql => npgsql
            // Retry up to 3 times with 10-second delay for transient faults (NFR-032)
            .EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null)
            // Lock to the installed PostgreSQL major version to avoid runtime negotiation overhead
            .SetPostgresVersion(new Version(16, 0))));

// ASP.NET Core Identity — RBAC with Patient / Staff / Admin roles (AC-2)
// Password policy: 8+ chars, upper, lower, digit, and special character.
// Lockout: 5 consecutive failures triggers a 30-minute lockout (NFR-016).
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // Password complexity
        options.Password.RequiredLength         = 8;
        options.Password.RequireUppercase       = true;
        options.Password.RequireLowercase       = true;
        options.Password.RequireDigit           = true;
        options.Password.RequireNonAlphanumeric = true;

        // Account lockout (NFR-016)
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(30);
        options.Lockout.AllowedForNewUsers      = true;

        // User settings
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Replace the default PBKDF2 hasher with BCrypt (work factor 10) per NFR-013.
// Registered AFTER AddIdentity so it overrides the default registration.
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, BcryptPasswordHasher>();

// JWT Bearer authentication — overrides Identity's default cookie scheme for API endpoints.
// TokenValidationParameters: validate issuer, audience, signing key, expiry; zero clock skew
// so the 15-minute access window is exact (AC-1).
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException(
        "JwtSettings configuration section is missing. "
        + "Ensure appsettings.json has a JwtSettings section and the signing key is set via "
        + "user secrets: dotnet user-secrets set \"JwtSettings:SigningKey\" \"<32+ char key>\"");

if (jwtSettings.SigningKey.Length < 32)
    throw new InvalidOperationException(
        "JwtSettings:SigningKey must be at least 32 characters (256 bits) for HMAC-SHA256.");

builder.Services.AddSingleton(jwtSettings); // Injected directly — no IOptions wrapper needed

builder.Services
    .AddAuthentication(options =>
    {
        // Override Identity's default cookie scheme so JWT is used for all API auth.
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme             = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtSettings.Issuer,
            ValidateAudience         = true,
            ValidAudience            = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.Zero, // Exact 15-minute window (no tolerance)
        };
    });

// Token service — scoped so it participates in per-request DI scopes correctly.
builder.Services.AddScoped<ITokenService, TokenService>();

// NpgsqlDataSource singleton — exposes the same pooled data source used by EF Core
// to downstream services that execute raw SQL (e.g. pgvector cosine queries).
// Registered as singleton so the Npgsql Vector type mapping and connection pool
// are shared across all requests (NFR-028, AC-AI-001).
builder.Services.AddSingleton(npgsqlDataSource);

// Vector search service — scoped per-request; executes raw Npgsql SQL for <=> cosine
// distance queries and RRF hybrid search over pgvector embedding tables (AIR-R02, AIR-R06).
builder.Services.AddScoped<IVectorSearchService, VectorSearchService>();

// Data seeder — scoped so it participates in the per-scope DI lifetime used during startup.
// Only invoked when the application is started with the '--seed' CLI argument in non-Production
// environments. No-op at runtime (never called during normal HTTP request handling).
builder.Services.AddScoped<IDataSeeder, SqlFileDataSeeder>();

// Redis / IDistributedCache — Upstash Redis with TLS; AbortOnConnectFail=false so the
// application starts even when Redis is temporarily unavailable (AC-4 cache-bypass requirement).
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? "localhost:6379,abortConnect=False,ssl=False";

var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
redisOptions.AbortOnConnectFail = false;
// Exponential backoff starting at 5 s — avoids thundering-herd on reconnect
redisOptions.ReconnectRetryPolicy = new ExponentialRetry(deltaBackOffMilliseconds: 5000);

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.InstanceName = "upacip:";          // Namespace all cache keys
    options.ConfigurationOptions = redisOptions;
});

// Cache service — singleton so Polly circuit breaker state persists across requests.
// Feature services inject ICacheService; the Redis implementation is swappable.
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Strongly-typed configuration validation — fails fast at startup if required fields
// are missing or out of range (fail-fast per TR-022 and 12-factor config principle).
// IOptionsMonitor<AppSettings> is available for injection in services that need
// hot-reload support (feature flags, log levels). Connection strings and Kestrel
// bindings still require a process restart after changes.
builder.Services
    .AddOptions<AppSettings>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Feature management — reads the FeatureManagement section from appsettings.json.
// Participates in the reloadOnChange pipeline so toggling a flag takes effect
// without restart (TR-021 / AC-4). DisabledFeaturesHandler returns a structured
// JSON 404 body instead of the default empty response.
builder.Services
    .AddFeatureManagement()
    .UseDisabledFeaturesHandler(new DisabledFeaturesHandler());

// Health checks — registered here so both DB and Redis connection strings are in scope.
// /health → liveness: Predicate = _ => false means no dependency probes; always 200 if the
//           process is alive and can serve requests.
// /ready  → readiness: only checks tagged "ready" (database + redis); returns 503 when any
//           dependency is unhealthy so the load balancer removes the instance from rotation.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(
        name: "database",
        tags: new[] { "ready" })
    .AddRedis(
        redisConnectionString,
        name: "redis",
        tags: new[] { "ready" },
        timeout: TimeSpan.FromSeconds(3));

// ---------- Pipeline ----------
var app = builder.Build();

// ---------- --seed CLI handler ----------
// When the application is started with the '--seed' argument (e.g.,
// `dotnet run --project src/UPACIP.Api -- --seed`), the seeder is invoked and
// the process exits WITHOUT starting the web server. This keeps seeding separate
// from the normal application lifecycle and prevents accidental seeding on every
// startup.
//
// Production guard: SqlFileDataSeeder.SeedAsync() checks IHostEnvironment.IsProduction()
// and returns without action if ASPNETCORE_ENVIRONMENT is 'Production'.
if (args.Contains("--seed"))
{
    var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();
    seedLogger.LogInformation("--seed flag detected. Running SqlFileDataSeeder and exiting.");

    using var seedScope = app.Services.CreateScope();
    var seeder = seedScope.ServiceProvider.GetRequiredService<IDataSeeder>();
    await seeder.SeedAsync();

    seedLogger.LogInformation("Seeding complete. Application exiting.");
    return;
}

// 1. Correlation ID — must be first so all subsequent middleware can use it
app.UseCorrelationId();

// 2. Global exception handler — wraps everything below so errors carry a correlation ID
app.UseGlobalExceptionHandler();

// 3. Swagger — developer tooling, registered early so exceptions are caught
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "UPACIP API v1");
        options.RoutePrefix = "swagger";
    });
}

// 4–7. Standard ASP.NET Core pipeline order
app.UseHttpsRedirection();
app.UseCors("ReactFrontend");
app.UseAuthentication(); // Must precede UseAuthorization to populate HttpContext.User
app.UseAuthorization();
app.MapControllers();

// Liveness — process-level check; no external dependency probes.
// Returns 200 as long as the application is running and able to accept requests.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate      = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
}).AllowAnonymous();

// Readiness — dependency-level check; only runs checks tagged "ready" (database + redis).
// Returns 503 if any dependency is unhealthy so upstream load balancers stop routing traffic.
var readyOptions = new HealthCheckOptions
{
    Predicate      = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
};
readyOptions.ResultStatusCodes[HealthStatus.Healthy]   = StatusCodes.Status200OK;
readyOptions.ResultStatusCodes[HealthStatus.Degraded]  = StatusCodes.Status200OK;
readyOptions.ResultStatusCodes[HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable;
app.MapHealthChecks("/ready", readyOptions).AllowAnonymous();

// ---------- Startup DB health check ----------
// Probe the database connection before accepting traffic.
// Logs a clear, actionable message instead of crashing with an unhandled exception.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        // OpenConnection / CloseConnection exercised; respects Npgsql retry policy above.
        await db.Database.CanConnectAsync();
        logger.LogInformation("Database connection established successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Unable to connect to PostgreSQL. Verify the service is running on {Host}:{Port} " +
            "and the connection string in appsettings.json (or user secrets) is correct. " +
            "Start PostgreSQL with: Start-Service postgresql-x64-18",
            "localhost", 5432);
        // Allow the app to start so health-check endpoints remain reachable.
    }
}

// ---------- Startup Redis health check ----------
// Probe Redis connectivity; logs a warning (not an error) when unavailable so the
// application continues with cache bypass per AC-4.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
    try
    {
        // A read attempt forces the first real connection; throws on unreachable Redis.
        await cache.GetAsync("__healthcheck__");
        var endpoint = redisOptions.EndPoints.Count > 0
            ? redisOptions.EndPoints[0].ToString()
            : "unknown";
        logger.LogInformation(
            "Redis connection established successfully (endpoint: {Endpoint}).", endpoint);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex,
            "Redis is unreachable at startup. The application will operate with cache bypass. " +
            "Verify Redis:ConnectionString in configuration or user secrets.");
        // Non-blocking: application continues without Redis.
    }
}

// ---------- Graceful shutdown logging ----------
// IHostApplicationLifetime events fire when the Windows Service receives a stop signal
// (or SIGTERM in console mode), giving in-flight requests time to complete.
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStopping.Register(() =>
    appLogger.LogInformation("Application stopping — draining in-flight requests."));

lifetime.ApplicationStopped.Register(() =>
    appLogger.LogInformation("Application stopped."));

app.Run();

