using FluentValidation;
using FluentValidation.AspNetCore;
using Serilog;
using UPACIP.Api.Features.AIGateway.Endpoints;
using UPACIP.Api.Features.AIGateway.Extensions;
using UPACIP.Api.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Pgvector;
using StackExchange.Redis;
using System.Net;
using System.Text;
using System.Text.Json;
using UPACIP.Api.Authorization;
using UPACIP.Api.Claims;
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
using UPACIP.Service.Appointments;
using UPACIP.Service.AI.NoShowRisk;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Notifications;
using UPACIP.Service.Documents;
using UPACIP.Service.AI.DocumentParsing;
using UPACIP.Service.AI.ClinicalExtraction;
using UPACIP.Service.AI.ConflictDetection;
using UPACIP.Service.Consolidation;
using UPACIP.Service.Conflict;
using UPACIP.Service.Profile;
using UPACIP.Service.AI;
using UPACIP.Service.Audit;
using UPACIP.Api.Filters;

var builder = WebApplication.CreateBuilder(args);

// Enable Windows Service lifecycle integration (start/stop/graceful shutdown signals).
// This is a no-op when running in console mode (dotnet run / development), so it does
// not affect the local developer workflow.
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "UPACIP.Api";
});

// ── Serilog logging pipeline with PII redaction (US_066 AC-2, NFR-017) ──────────────────────
// UseSerilog three-parameter overload receives the IServiceProvider *after* all services are
// registered, so PiiRedactionEnricher and PiiDestructuringPolicy can be resolved from DI.
// ReadFrom.Configuration picks up the "Serilog" section in appsettings.json for minimum levels.
// ReadFrom.Services wires any Serilog components registered in the container (EC-2 extensibility).
builder.Host.UseSerilog((ctx, services, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.With(services.GetRequiredService<PiiRedactionEnricher>())
       .Destructure.With(services.GetRequiredService<PiiDestructuringPolicy>()));

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
// ValidateModelAttribute is registered as a global filter so all controllers benefit
// from structured 400 responses without per-controller attribute decoration (US_066 AC-4).
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidateModelAttribute>();
});
builder.Services.AddEndpointsApiExplorer();

// FluentValidation — auto-validates request DTOs before controller actions execute.
// Validators are discovered from the UPACIP.Service assembly via assembly scanning.
// When validation fails, the default ValidationProblemDetails (RFC 7807) response is
// replaced by our ErrorResponse model so all 400 responses share a consistent shape.
builder.Services
    .AddFluentValidationAutoValidation()
    .AddValidatorsFromAssemblyContaining<AppointmentDateValidator>()  // UPACIP.Service validators
    .AddValidatorsFromAssemblyContaining<UPACIP.Api.Validation.SelectValueRequestDtoValidator>(); // UPACIP.Api validators

// Override the default 400 response factory so FluentValidation errors use ErrorResponse
// (same shape as constraint/exception errors) instead of ValidationProblemDetails.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Suppress the built-in [ApiController] automatic 400 response so that
    // ValidateModelAttribute (global filter) takes sole ownership of validation error
    // responses and returns the structured ValidationErrorResponse shape (US_066 AC-4).
    options.SuppressModelStateInvalidFilter = true;

    // Retained as fallback for any code path that bypasses the global filter.
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
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "UPACIP API",
        Version = "v1",
        Description = "Unified Patient Access & Clinical Intelligence Platform – Backend API"
    });

    // JWT Bearer security definition — enables the Authorize button in Swagger UI (NFR-038).
    var jwtScheme = new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Enter your JWT access token (without the 'Bearer' prefix).",
        Reference    = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtScheme, Array.Empty<string>() },
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

// Custom 401/403 response handler — returns structured JSON instead of empty responses
// and writes 403 events to the audit trail (NFR-012).
// Uses JwtBearerEvents so no external interface reference is needed.
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

        // Override default empty 401/403 responses with structured JSON (NFR-012).
        // OnChallenge fires for unauthenticated requests (no valid JWT / expired token).
        // OnForbidden fires when an authenticated user fails a policy check.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Check JWT jti blacklist — rejects tokens that were revoked on logout
                // or session invalidation before their natural expiry (AC-1, task_002 step 5).
                var jti = context.Principal?
                    .FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;

                if (!string.IsNullOrEmpty(jti))
                {
                    var tokenService = context.HttpContext.RequestServices
                        .GetRequiredService<ITokenService>();

                    if (await tokenService.IsJtiBlacklistedAsync(jti, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Token has been revoked.");
                    }
                }
            },

            OnChallenge = async context =>
            {
                // Suppress the default WWW-Authenticate challenge and write our own body.
                context.HandleResponse();

                var logger       = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var scopeFactory = context.HttpContext.RequestServices
                    .GetRequiredService<IServiceScopeFactory>();

                await AuthorizationResultHandler.HandleChallengedAsync(
                    context.HttpContext, logger);
            },

            OnForbidden = async context =>
            {
                var logger       = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var scopeFactory = context.HttpContext.RequestServices
                    .GetRequiredService<IServiceScopeFactory>();

                await AuthorizationResultHandler.HandleForbiddenAsync(
                    context.HttpContext, logger, scopeFactory);
            },
        };
    });

// ── RBAC Authorization policies (AC-1, AC-2, AC-3, AC-4) ─────────────────────
// Named policies map directly to the three application roles.  Controllers reference
// these by name via [Authorize(Policy = RbacPolicies.XXX)] so role strings are never
// scattered as raw literals across the codebase.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(RbacPolicies.PatientOnly,
        policy => policy.RequireRole("Patient"));

    options.AddPolicy(RbacPolicies.StaffOnly,
        policy => policy.RequireRole("Staff"));

    options.AddPolicy(RbacPolicies.AdminOnly,
        policy => policy.RequireRole("Admin"));

    options.AddPolicy(RbacPolicies.StaffOrAdmin,
        policy => policy.RequireRole("Staff", "Admin"));

    options.AddPolicy(RbacPolicies.AnyAuthenticated,
        policy => policy.RequireAuthenticatedUser());
});

// Claims transformer — normalizes short-form "role" claims to ClaimTypes.Role so that
// [Authorize(Policy)] checks work with tokens from any identity provider.
builder.Services.AddScoped<IClaimsTransformation, RoleClaimsTransformer>();

// Token service — scoped so it participates in per-request DI scopes correctly.
builder.Services.AddScoped<ITokenService, TokenService>();

// Session management — Redis-backed active session tracking with 15-min sliding TTL (NFR-014, FR-003).
// Scoped per-request because it depends on ICacheService (singleton) via constructor injection.
builder.Services.AddScoped<ISessionService, RedisSessionService>();

// Concurrent session guard — scoped; depends on ISessionService.
builder.Services.AddScoped<ConcurrentSessionGuard>();

// Account lockout audit handler — scoped; wraps IAuditLogService with structured lockout metadata
// logging (US_065 AC-3, NFR-016). Separates lockout audit concerns from AuthController.
builder.Services.AddScoped<IAccountLockoutAuditHandler, AccountLockoutAuditHandler>();

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

// Admin seed service — IHostedService that runs on every startup (non-Production only).
// Creates the default admin account if it does not yet exist (idempotent).
// Credentials are read from DefaultAdmin:Email / DefaultAdmin:Password in configuration.
// Production is guarded inside AdminSeedService itself (defence-in-depth).
builder.Services.AddHostedService<AdminSeedService>();

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

// Registration service — scoped per-request (depends on scoped DbContext and UserManager).
builder.Services.AddScoped<IRegistrationService, RegistrationService>();

// Email service — scoped; MailKit SmtpClient is instantiated per-send, so scoped is correct.
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// ── EP-005 SMTP transport layer (task_001_be_smtp_provider_integration) ─────────────────
// Binds the EmailProvider configuration section (primary = SendGrid, fallback = Gmail).
// ValidateDataAnnotations ensures required fields are present at startup (fail-fast).
// IEmailTransport is registered as Scoped — MailKit SmtpClient is instantiated per-send;
// scoped lifetime is correct and consistent with the existing IEmailService registration.
builder.Services
    .AddOptions<EmailProviderOptions>()
    .Bind(builder.Configuration.GetSection(EmailProviderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddScoped<IEmailTransport, SmtpEmailTransport>();

// ── EP-005 notification email orchestration (task_002_be_notification_email_composition_and_logging)
// EmailTemplateRenderer is stateless — singleton avoids allocation on every request.
// NotificationEmailService is scoped because it depends on the scoped ApplicationDbContext.
builder.Services.AddSingleton<EmailTemplateRenderer>();
builder.Services.AddScoped<INotificationEmailService, NotificationEmailService>();

// ── EP-005 notification delivery reliability (US_037 task_001) ──────────────────────────────────
// BufferedNotificationLogWriter is Singleton — holds the in-memory log-entry buffer (max 1000)
// that keeps attempt records safe when persistence is temporarily unavailable (EC-1).
// NotificationDeliveryReliabilityService is Scoped — orchestrates retry scheduling, permanent-failure
// marking, and patient contact flagging; depends on the scoped ApplicationDbContext.
// NotificationRetryWorker is Singleton — registered both as INotificationRetryQueue (for scoped
// callers to enqueue retries) and as IHostedService (for the 1-minute PeriodicTimer loop).
builder.Services.AddSingleton<BufferedNotificationLogWriter>();
builder.Services.AddScoped<INotificationDeliveryReliabilityService, NotificationDeliveryReliabilityService>();
builder.Services.AddSingleton<NotificationRetryWorker>();
builder.Services.AddSingleton<INotificationRetryQueue>(sp => sp.GetRequiredService<NotificationRetryWorker>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<NotificationRetryWorker>());

// ── EP-005 admin notification log query (US_037 task_003_be_notification_log_admin_query_and_statistics_api)
// NotificationLogQueryService is Scoped — depends on the scoped ApplicationDbContext.
// Exposed via AdminNotificationLogController (AdminOnly policy).
builder.Services.AddScoped<INotificationLogQueryService, NotificationLogQueryService>();

// ── EP-005 SMS transport layer (US_033 task_001_be_twilio_provider_integration) ────────────────
// Binds the SmsProvider configuration section (Twilio credentials, US-number scope, gateway toggle).
// ValidateDataAnnotations ensures required fields are present at startup (fail-fast).
// TwilioSmsTransport is registered as Scoped — consistent with the email transport lifetime.
builder.Services
    .AddOptions<SmsProviderOptions>()
    .Bind(builder.Configuration.GetSection(SmsProviderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddScoped<ISmsTransport, TwilioSmsTransport>();

// ── EP-005 SMS orchestration layer (US_033 task_002_be_notification_sms_orchestration_and_logging) ──
// NotificationSmsService is Scoped — it depends on the scoped ApplicationDbContext and
// honours patient opt-out preference before invoking the Twilio transport.
builder.Services.AddScoped<INotificationSmsService, NotificationSmsService>();

// ── EP-005 booking confirmation orchestration (US_034 task_001 + task_002) ──────────────────
// QrCodeService is Singleton — stateless pure-C# QR renderer; no DI dependencies.
// PdfConfirmationService is Scoped — depends on IQrCodeService and ILogger.
// BookingConfirmationNotificationService is Scoped — depends on ApplicationDbContext,
// email/SMS services, and IPdfConfirmationService.
builder.Services.AddSingleton<IQrCodeService, QrCodeService>();
builder.Services.AddScoped<IPdfConfirmationService, PdfConfirmationService>();
builder.Services.AddScoped<IBookingConfirmationNotificationService, BookingConfirmationNotificationService>();
// ── EP-005 reminder batch scheduling (US_035 task_001 + task_002) ──────────────────────────
// ReminderNotificationService — Scoped: orchestrates email + SMS dispatch per appointment
// (US_035 task_002_be_reminder_notification_dispatch_and_skip_handling). Replaces stub.
// IReminderBatchSchedulerService / ReminderBatchSchedulerService — Scoped: checkpoint-aware batch runner.
// ReminderBatchWorker — Singleton BackgroundService: creates a fresh DI scope per batch run (EC-1).
builder.Services.AddScoped<IReminderNotificationService, ReminderNotificationService>();
builder.Services.AddScoped<IReminderBatchSchedulerService, ReminderBatchSchedulerService>();
builder.Services.AddHostedService<ReminderBatchWorker>();

builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, PasswordComplexityValidator>();

// Password reset service — token generation, validation, post-reset session cleanup (US_015).
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// MFA service — TOTP secret generation, AES-256 encryption, backup code hashing (US_016 AC-1).
builder.Services.AddScoped<IMfaService, MfaService>();

// Audit log service — append-only auth event logging (US_016 AC-5).
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// ── US_066 Task 2 — PII Redaction Logging (AC-2, NFR-017, EC-2) ─────────────────────────────
// PiiRedactionOptions: strongly-typed config for PII field names and regex patterns.
// PiiRedactionEnricher: Singleton ILogEventEnricher — masks scalar string properties.
// PiiDestructuringPolicy: Singleton IDestructuringPolicy — masks PII in {@Object} destructuring.
// Both are Singleton because:
//   - They hold a pre-computed HashSet<string> built from config (no per-request state).
//   - Serilog enrichers/policies are shared across the entire logging pipeline.
builder.Services
    .AddOptions<PiiRedactionOptions>()
    .Bind(builder.Configuration.GetSection(PiiRedactionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<PiiRedactionEnricher>();
builder.Services.AddSingleton<PiiDestructuringPolicy>();

// ── EP-011 Audit Log Query API (US_064) ──────────────────────────────────────────────────────// AuditSettings: configured query page limits and retention period (NFR-040, NFR-043).
// AuditLogQueryService: CQRS read-side — filtered, paginated, AsNoTracking reads (AC-3, TR-013).
// IClientInfoAccessor: extracts client IP (X-Forwarded-For-aware) and User-Agent (AC-1, NFR-018).
// AuditLoggingActionFilter: cross-cutting filter that logs state-changing requests (POST/PUT/PATCH/DELETE).
builder.Services
    .AddOptions<AuditSettings>()
    .Bind(builder.Configuration.GetSection(AuditSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
builder.Services.AddScoped<IClientInfoAccessor, ClientInfoAccessor>();
builder.Services.AddScoped<AuditLoggingActionFilter>();

// ── EP-011 Audit Failover Queue (US_064 task_003) ────────────────────────────────────────────
// AuditQueueSettings: configures Redis key, batch size, intervals, and Polly thresholds.
// AuditQueueService: Singleton — stateless Redis RPUSH/LPOP/LLEN with local-file last-resort.
// AuditQueueFlushWorker: hosted BackgroundService — drains queue to PostgreSQL with Polly
//   retry (3x exponential) + circuit breaker (5 failures / 30 s open) (NFR-032).
builder.Services
    .AddOptions<AuditQueueSettings>()
    .Bind(builder.Configuration.GetSection(AuditQueueSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IAuditQueueService, AuditQueueService>();
builder.Services.AddHostedService<AuditQueueFlushWorker>();

// ── EP-006 Clinical Document Upload (US_038 task_002) ────────────────────────────────────────
// EncryptionOptions: bound from Security:Encryption — holds the AES-256 key used by
//   FileEncryptionService. Key validation (32-byte Base64) is enforced at startup (US_063 AC-2).
// FileEncryptionService: Singleton — stateless pure-crypto AES-256-CBC service; owns the key.
//   Decoupled from file I/O so it can be unit-tested and reused independently.
// DocumentStorageSettings: storage root path from configuration (key moved to EncryptionOptions).
// EncryptedFileStorageService: Singleton — stateless file I/O; delegates crypto to IFileEncryptionService.
// ClinicalDocumentUploadService: Scoped — depends on scoped ApplicationDbContext.
builder.Services
    .AddOptions<EncryptionOptions>()
    .Bind(builder.Configuration.GetSection(EncryptionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IFileEncryptionService, FileEncryptionService>();
builder.Services
    .AddOptions<DocumentStorageSettings>()
    .Bind(builder.Configuration.GetSection(DocumentStorageSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IEncryptedFileStorageService, EncryptedFileStorageService>();

// ── EP-006 Document Parsing Queue Orchestration (US_039 task_002) ────────────────────────────
// IConnectionMultiplexer: Singleton — shared Redis connection for raw list operations (RPUSH/LPOP).
// Reuses the same ConfigurationOptions already built for IDistributedCache to avoid duplicate
// connections. AbortOnConnectFail=false means startup succeeds even when Redis is temporarily down.
var connectionMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
builder.Services.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);

// DocumentParsingDispatcherSettings: bound from "DocumentParsing" config section (EC-2).
// Defaults: MaxConcurrentJobs=5, PollingIntervalSeconds=5, MaxRetryAttempts=3.
builder.Services
    .AddOptions<DocumentParsingDispatcherSettings>()
    .Bind(builder.Configuration.GetSection(DocumentParsingDispatcherSettings.SectionName));

// DocumentParsingPromptBuilder + DocumentParsingResultValidator: Scoped helpers for
// building AI prompts and validating/sanitising model responses (US_039 task_003).
builder.Services.AddScoped<DocumentParsingPromptBuilder>();
builder.Services.AddScoped<DocumentParsingResultValidator>();

// Clinical extraction helpers (US_040 task_001):
// ClinicalExtractionPromptBuilder + ClinicalExtractionResultValidator + ClinicalExtractionLanguageGate
// are scoped to isolate per-request circuit-breaker state.
builder.Services.AddScoped<ClinicalExtractionPromptBuilder>();
builder.Services.AddScoped<ClinicalExtractionResultValidator>();
builder.Services.AddScoped<ClinicalExtractionLanguageGate>();
builder.Services.AddScoped<ClinicalExtractionService>();

// Extraction persistence (US_040 task_002): converts AI extraction envelopes into ExtractedData rows.
builder.Services.AddScoped<IExtractedDataPersistenceService, ExtractedDataPersistenceService>();

// Extracted-data verification (US_041 task_002): single-row verify/correct + bulk verification.
builder.Services.AddScoped<IExtractedDataVerificationService, ExtractedDataVerificationService>();

// Document preview (US_042 task_002): annotation metadata + secure content streaming.
builder.Services.AddScoped<IDocumentPreviewService, DocumentPreviewService>();

// IDocumentParserWorker: Scoped — executes AI parsing via OpenAI (primary) / Anthropic (fallback).
// Replaced NullDocumentParserWorker after US_039 task_003 implementation.
builder.Services.AddScoped<IDocumentParserWorker, DocumentParsingWorker>();

// IDocumentParsingQueueService: Scoped — transitions document to Queued, enqueues Redis job,
// handles EC-1 Redis-unavailable fallback.
builder.Services.AddScoped<IDocumentParsingQueueService, DocumentParsingQueueService>();

// ClinicalDocumentUploadService: Scoped — now depends on IDocumentParsingQueueService.
builder.Services.AddScoped<IClinicalDocumentUploadService, ClinicalDocumentUploadService>();

// DocumentReplacementService: Scoped — orchestrates document replacement lifecycle (US_042 AC-2, AC-3, EC-1, EC-2).
builder.Services.AddScoped<IDocumentReplacementService, DocumentReplacementService>();

// DocumentParsingDispatcher: Singleton BackgroundService — FIFO Redis dequeue + Polly retry (EC-2).
builder.Services.AddHostedService<DocumentParsingDispatcher>();

// ── EP-012 Document Parsing Queue Monitor (US_071 TASK_003) ─────────────────────────────────
// AiQueueOptions: bound from "AiQueue" config section — monitor interval, stale threshold,
//   and escalation depth (AC-4). ValidateOnStart enforces sane defaults at startup.
// IDocumentParsingQueue / RedisDocumentParsingQueue: Singleton — thin Redis abstraction
//   (LLEN, LINDEX 0) used exclusively by the monitor for read-only health checks; soft-fail
//   on transient Redis errors so monitoring never impacts normal request handling.
// IQueueMonitorService / QueueMonitorService: Singleton — checks stale item age (>5 min LogWarning)
//   and depth escalation (>50 LogCritical) on each monitoring tick (AC-4).
// QueueMonitorJob: Singleton BackgroundService — drives the monitor via a 60-second PeriodicTimer.
builder.Services
    .AddOptions<UPACIP.Service.Documents.AiQueueOptions>()
    .Bind(builder.Configuration.GetSection(UPACIP.Service.Documents.AiQueueOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<UPACIP.Service.Documents.IDocumentParsingQueue, UPACIP.Service.Documents.RedisDocumentParsingQueue>();
builder.Services.AddSingleton<UPACIP.Service.Documents.IQueueMonitorService, UPACIP.Service.Documents.QueueMonitorService>();
builder.Services.AddHostedService<UPACIP.Service.Documents.QueueMonitorJob>();

// Appointment slot service — slot availability queries with Redis cache-aside (US_017 AC-1, AC-4).
builder.Services.AddScoped<IAppointmentSlotService, AppointmentSlotService>();

// Slot hold service — Redis-backed 60-second TTL slot reservation (US_018 AC-3).
builder.Services.AddScoped<ISlotHoldService, SlotHoldService>();

// Appointment booking service — optimistic-locking booking with Polly retry (US_018 AC-1, EC-1).
builder.Services.AddScoped<IAppointmentBookingService, AppointmentBookingService>();

// Appointment cancellation service — 24-hour UTC policy, slot release, audit log (US_019 AC-1–AC-4).
builder.Services.AddScoped<IAppointmentCancellationService, AppointmentCancellationService>();

// Waitlist orchestration — registration, offer dispatch, and claim-link redemption (US_020).
// WaitlistOfferProcessor is registered as both IWaitlistOfferQueue (singleton) and IHostedService
// so the same channel instance is shared between the cancellation enqueue path and the processor.
builder.Services.AddScoped<IWaitlistOfferNotificationService, WaitlistOfferNotificationService>();
builder.Services.AddSingleton<WaitlistOfferProcessor>();
builder.Services.AddSingleton<IWaitlistOfferQueue>(sp => sp.GetRequiredService<WaitlistOfferProcessor>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<WaitlistOfferProcessor>());
builder.Services.AddScoped<IWaitlistService, WaitlistService>();

// Preferred-slot swap engine (US_021) — evaluates freed slots against waiting patients'
// preferred criteria and auto-swaps or sends manual confirmation offers.
builder.Services.AddScoped<ISlotSwapNotificationService, SlotSwapNotificationService>();
builder.Services.AddSingleton<PreferredSlotSwapProcessor>();
builder.Services.AddSingleton<IPreferredSlotSwapQueue>(sp => sp.GetRequiredService<PreferredSlotSwapProcessor>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PreferredSlotSwapProcessor>());
builder.Services.AddScoped<IPreferredSlotSwapService, PreferredSlotSwapService>();

// Walk-in registration (US_022) — staff-only same-day booking with queue insertion.
builder.Services.AddScoped<IWalkInRegistrationService, WalkInRegistrationService>();

// Patient appointment rescheduling (US_023) — atomic slot swap with rule enforcement.
builder.Services.AddScoped<IAppointmentReschedulingService, AppointmentReschedulingService>();

// Patient appointment history (US_024) — paginated history with sort and all-status visibility.
builder.Services.AddScoped<IAppointmentHistoryService, AppointmentHistoryService>();

// Clinic settings singleton — name, IANA timezone ID, and iCal domain used by the calendar export.
// Registered before AppointmentCalendarService so DI resolves ClinicSettings as a constructor argument.
var clinicSettings = builder.Configuration.GetSection("ClinicSettings").Get<ClinicSettings>() ?? new ClinicSettings();
builder.Services.AddSingleton(clinicSettings);

// Appointment calendar export service — generates RFC 5545 .ics files for confirmed appointments (US_025, FR-025, TR-026).
builder.Services.AddScoped<IAppointmentCalendarService, AppointmentCalendarService>();

// No-show risk scoring engine — in-process classification model with rule-based fallback
// and Polly circuit breaker (AIR-006, AIR-O04, FR-014, US_026).
// FeatureExtractor and FallbackPolicy are registered as scoped so they share the DbContext scope.
// ScoringService is scoped; the Polly circuit breaker is stored as an instance field on the service
// so breaker state is scoped to the DI container lifetime (per-request isolation).
builder.Services.AddScoped<NoShowRiskFeatureExtractor>();
builder.Services.AddScoped<NoShowRiskFallbackPolicy>();
builder.Services.AddScoped<INoShowRiskScoringService, NoShowRiskScoringService>();

// No-show risk orchestrator — coordinates score calculation, persistence, and downstream
// integration for booking workflows, staff schedule, and arrival queue (US_026, AC-1, EC-1).
builder.Services.AddScoped<NoShowRiskOrchestrator>();

// ── US_067 Task 1 — AI Gateway Scaffold (AC-1, AC-3, AIR-O01, AIR-O02, AIR-O03) ──────────────
// Registers AIGatewayOptions, AIRequestValidationMiddleware, AIAuthenticationMiddleware,
// AIResponseNormalizationMiddleware, and IAIGatewayService / AIGatewayService.
// Provider adapters (IAIProviderAdapter) are registered in Task 002.
builder.Services.AddAIGateway(builder.Configuration);

// ── AI Conversational Intake services (AIR-001, FR-026, US_027) ───────────────────────────
// AiGatewaySettings bound from configuration; never logged.
builder.Services.Configure<AiGatewaySettings>(
    builder.Configuration.GetSection(AiGatewaySettings.SectionName));

// Named HttpClient for OpenAI — base address + auth header preset; timeout from config.
var aiSettings = builder.Configuration.GetSection(AiGatewaySettings.SectionName).Get<AiGatewaySettings>() ?? new AiGatewaySettings();
builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri(aiSettings.OpenAiBaseUrl);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiSettings.OpenAiApiKey);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(aiSettings.TimeoutSeconds > 0 ? aiSettings.TimeoutSeconds : 10);
});

// Named HttpClient for Anthropic Claude (fallback provider).
builder.Services.AddHttpClient("anthropic", client =>
{
    client.BaseAddress = new Uri(aiSettings.AnthropicBaseUrl);
    client.DefaultRequestHeaders.Add("x-api-key", aiSettings.AnthropicApiKey);
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(aiSettings.TimeoutSeconds > 0 ? aiSettings.TimeoutSeconds : 10);
});

// Intake service components — scoped so they share the per-request DI scope.
builder.Services.AddScoped<IntakeRagRetriever>();
builder.Services.AddScoped<IntakeFieldExtractionValidator>();
builder.Services.AddScoped<IntakePromptBuilder>();
builder.Services.AddScoped<IConversationalIntakeService, ConversationalIntakeService>();

// AI intake session service — scoped per-request; coordinates session lifecycle,
// Redis-backed state persistence, and IntakeData completion (US_027, AC-1–AC-5).
builder.Services.AddScoped<IAIIntakeSessionService, AIIntakeSessionService>();

// Manual intake form service — draft load, autosave, submit, and idempotent completion (US_028, FR-027–FR-031).
builder.Services.AddScoped<IManualIntakeService, ManualIntakeService>();

// Intake mode-switch orchestration — bidirectional AI ↔ manual merge with conflict detection (US_029, FR-028).
builder.Services.AddScoped<IIntakeModeSwitchService, IntakeModeSwitchService>();

// Intake autosave — 30-second boundary heartbeat and EC-1 idempotency for both AI and manual surfaces (US_030, FR-035).
builder.Services.AddScoped<IIntakeAutosaveService, IntakeAutosaveService>();
builder.Services.AddScoped<IInsurancePrecheckService, InsurancePrecheckService>();

// Patient profile consolidation — merges extracted clinical data into versioned unified profiles (US_043, FR-052, FR-056).
builder.Services.AddScoped<IConsolidationService, ConsolidationService>();
builder.Services.AddHostedService<ConsolidationWorker>();

// AI conflict detection — GPT-4o-mini/Claude fallback LLM analysis of medication contraindications and
// conflicting diagnoses after each consolidation run (US_043 task_004, AIR-005, AIR-S09, AIR-S10, AIR-Q07).
builder.Services.AddScoped<IConflictDetectionService, ConflictDetectionService>();

// Clinical conflict management — persists, escalates, resolves, and queues AI-detected conflicts
// (US_044, AC-1, AC-3, AC-4, FR-053).
builder.Services.AddScoped<IConflictManagementService, ConflictManagementService>();

// Staff conflict resolution workflow — value selection, both-valid preservation, profile
// verification lifecycle, and resolution progress tracking (US_045, AC-2, AC-4, EC-1, EC-2, FR-054).
builder.Services.AddScoped<IConflictResolutionService, ConflictResolutionService>();

// Patient profile aggregation — 360° profile retrieval, version history, source citations, and manual consolidation trigger (US_043, AC-1, AC-2, AC-3, FR-052, FR-056).
builder.Services.AddScoped<IPatientProfileService, PatientProfileService>();

// Patient search service — staff-only patient search with Redis cache-aside (US_062 AC-1, NFR-030).
builder.Services.AddScoped<IPatientSearchService, PatientSearchService>();

// ── Manual fallback workflow (US_046, AC-1, AC-2, AC-3, AC-4) ───────────────────────────────
// ConsolidationConfidenceService: evaluates AI confidence thresholds, returns low-confidence
// items, and persists manual verification batches with audit logging (AC-1, AC-3, FR-093).
// DateValidationService: chronological plausibility validator that annotates ExtractedData
// rows with date violations and incomplete-date flags (AC-2, edge case).
// AiHealthCheckService: Redis-cached AI gateway availability (AC-4, NFR-030). Singleton so
// the 5-minute TTL window is shared across all requests; depends only on ICacheService (singleton).
// ConfidenceThresholdGate: pure-computation confidence evaluator; Singleton (stateless).
// AiAuditLogger: structured Serilog audit logging for all AI interactions (AIR-S04). Singleton.
builder.Services.AddScoped<IConsolidationConfidenceService, ConsolidationConfidenceService>();
builder.Services.AddScoped<IDateValidationService, DateValidationService>();
builder.Services.AddSingleton<IAiHealthCheckService, AiHealthCheckService>();
builder.Services.AddSingleton<IConfidenceThresholdGate, ConfidenceThresholdGate>();
builder.Services.AddSingleton<AiAuditLogger>();

// ── ICD-10 coding pipeline (US_047, AC-1, AC-3, AC-4, FR-063) ────────────────────────────────
// AiCodingGateway (task_003): production AI gateway calling OpenAI GPT-4o-mini (primary) and
//   Anthropic Claude 3.5 Sonnet (fallback) with Polly circuit breaker + exponential retry (AIR-O04).
// Icd10RagRetriever: Scoped — generates embeddings and queries pgvector CodingGuideline index (AIR-R01).
// CodingGuardrailsService: Scoped — PII redaction, ICD-10 format validation, library cross-reference (AIR-S01/S02).
// Icd10ResponseParser: Scoped — parses LLM JSON tool-call response into AiCodeSuggestion objects.
// Icd10PromptBuilder: Singleton — stateless Liquid template loader; safe to share across requests.
// IIcd10CodingService: Scoped — reads ExtractedData and writes MedicalCode rows.
// IIcd10LibraryService: Scoped — manages Icd10CodeLibrary rows and revalidation lifecycle.
// Icd10CodingWorker: BackgroundService draining the Redis coding queue (NFR-029).
builder.Services.AddScoped<UPACIP.Service.AI.Coding.Icd10RagRetriever>();
builder.Services.AddScoped<UPACIP.Service.AI.Coding.CodingGuardrailsService>();
builder.Services.AddScoped<UPACIP.Service.AI.Coding.Icd10ResponseParser>();
builder.Services.AddSingleton<UPACIP.Service.AI.Coding.Icd10PromptBuilder>();
builder.Services.AddScoped<UPACIP.Service.Coding.IAiCodingGateway, UPACIP.Service.AI.Coding.AiCodingGateway>();
builder.Services.AddScoped<UPACIP.Service.Coding.IIcd10CodingService, UPACIP.Service.Coding.Icd10CodingService>();
builder.Services.AddScoped<UPACIP.Service.Coding.IIcd10LibraryService, UPACIP.Service.Coding.Icd10LibraryService>();
builder.Services.AddHostedService<UPACIP.Service.Coding.Icd10CodingWorker>();

// ── CPT coding pipeline (US_048, AC-1, AC-3, AC-4, FR-066) ───────────────────────────────────
// ICptCodingService: Scoped — manages approve/override actions on AI-suggested CPT MedicalCode rows.
//   AI generation pipeline (task_004_ai_cpt_prompt_rag) will be registered here once the RAG
//   retrieval layer and CPT prompt templates are ready.
// ICptCodeLibraryService: Scoped — manages CptCodeLibrary rows and quarterly revalidation lifecycle.
builder.Services.AddScoped<UPACIP.Service.Coding.ICptCodingService, UPACIP.Service.Coding.CptCodingService>();
builder.Services.AddScoped<UPACIP.Service.Coding.ICptCodeLibraryService, UPACIP.Service.Coding.CptCodeLibraryService>();

// CPT AI generation pipeline (task_004_ai_cpt_prompt_rag):
//   ICptGenerationService: Scoped — orchestrates AI gateway, library validation, bundle detection, persistence.
//   CptPromptBuilder: Singleton — stateless template loader (matches Icd10PromptBuilder registration pattern).
//   CptRagRetriever: Scoped — pgvector similarity search (uses scoped IVectorSearchService).
//   CptResponseParser: Scoped — JSON response parser + guardrails validation.
//   CptCodingWorker: Singleton BackgroundService — drains upacip:cpt-coding-queue.
builder.Services.AddScoped<UPACIP.Service.Coding.ICptGenerationService, UPACIP.Service.Coding.CptGenerationService>();
builder.Services.AddSingleton<UPACIP.Service.AI.Coding.CptPromptBuilder>();
builder.Services.AddScoped<UPACIP.Service.AI.Coding.CptRagRetriever>();
builder.Services.AddScoped<UPACIP.Service.AI.Coding.CptResponseParser>();
builder.Services.AddHostedService<UPACIP.Service.Coding.CptCodingWorker>();

// ── Code verification service (US_049, AC-1 through AC-4, EC-1, EC-2, FR-064) ───────────────
// ICodeVerificationService: Scoped — approval/override lifecycle, deprecated-code blocking,
//   immutable CodingAuditLog creation, verification progress tracking, and code library search.
builder.Services.AddScoped<UPACIP.Service.Coding.ICodeVerificationService, UPACIP.Service.Coding.CodeVerificationService>();

// ── Agreement rate service (US_050, AC-1 through AC-4, FR-067, FR-068, AIR-Q09) ────────────
// IAgreementRateService: Scoped — daily rate calculation, discrepancy detection, and alert
//   generation.  Requires ApplicationDbContext → registered as Scoped.
// AgreementRateCalculationJob: BackgroundService — fires every 24 hours, resolves
//   IAgreementRateService in a fresh scope per execution (NFR-032 retry with backoff).
builder.Services.AddScoped<UPACIP.Service.AgreementRate.IAgreementRateService, UPACIP.Service.AgreementRate.AgreementRateService>();
builder.Services.AddHostedService<UPACIP.Service.AgreementRate.AgreementRateCalculationJob>();

// ── EP-013 AI Metrics Monitoring (US_072 task_002) ──────────────────────────────────────────
// IAiMetricsService: Scoped — accuracy aggregation from MedicalCode/ExtractedData, latency
//   percentile aggregation from AI Gateway logs, and alert generation on threshold breaches.
// AiMetricsCalculationJob: BackgroundService — fires every 24 h (runs once on startup);
//   creates a fresh IServiceScope per execution so scoped services are isolated and disposed.
//   Retry: up to 3 attempts with exponential backoff (5 s, 25 s, 125 s) per NFR-032.
builder.Services.AddScoped<UPACIP.Service.AiMetrics.IAiMetricsService, UPACIP.Service.AiMetrics.AiMetricsService>();
builder.Services.AddHostedService<UPACIP.Service.AiMetrics.AiMetricsCalculationJob>();

// ── EP-013 Confidence Score Calibration (US_073 task_002) ───────────────────────────────────
// ICalibrationService: Scoped — Platt-scaling parameter lookup, score transformation,
//   low-confidence flagging (<0.80 → FlaggedForReview), per-category calibration, and
//   drift alert generation. Falls back to CalibrationPending when insufficient data exists.
// CalibrationJob: BackgroundService — fires every Calibration:IntervalDays days (default: 7);
//   runs once on startup; resolves a fresh IServiceScope per execution so scoped services
//   are isolated and disposed. Retry: up to 3 attempts with exponential backoff (5s, 25s, 125s)
//   per NFR-032.
builder.Services.AddScoped<UPACIP.Service.Calibration.ICalibrationService, UPACIP.Service.Calibration.CalibrationService>();
builder.Services.AddHostedService<UPACIP.Service.Calibration.CalibrationJob>();

// ── EP-013 PII Redaction Pipeline (US_074 task_001, AC-3, AIR-S01) ──────────────────────────
// IPiiRedactionService: Singleton — stateless regex-based detection and placeholder
//   tokenisation. Scans all six PII categories (SSN, email, phone, DOB, address, name)
//   before prompt dispatch to external AI providers. Medical term allowlist prevents
//   false-positive redaction of common eponyms (AC-3).
// PiiRedactionMiddleware is registered inside AddAIGateway() as it is a gateway pipeline
//   component. IPiiRedactionService must be registered BEFORE AddAIGateway() is called so
//   that PiiRedactionMiddleware can resolve it from the container at startup.
builder.Services.AddSingleton<UPACIP.Service.AiSafety.IPiiRedactionService, UPACIP.Service.AiSafety.PiiRedactionService>();

// ── EP-013 Hallucination Tracking (US_074 task_002, AC-1, AC-2, AIR-Q06) ────────────────────
// IHallucinationTrackingService: Scoped — records staff verification outcomes (Supported /
//   Unsupported / PartiallySupported) against AI-generated medical justifications; handles
//   retroactive hallucination detection (resets ApprovedByUserId, creates retroactive alert);
//   calculates daily hallucination rate; runs the full aggregation cycle on demand (AC-1, AC-2).
// HallucinationAggregationJob: BackgroundService — fires every 24 hours (+ on startup);
//   aggregates the previous day's hallucination rate into HallucinationMetric and generates
//   a HallucinationAlert with model-review recommendation when rate > 5% (AC-2, AIR-Q06).
//   Retry: up to 3 attempts with exponential backoff (5s, 25s, 125s) per NFR-032.
builder.Services.AddScoped<UPACIP.Service.AiSafety.IHallucinationTrackingService, UPACIP.Service.AiSafety.HallucinationTrackingService>();
builder.Services.AddHostedService<UPACIP.Service.AiSafety.HallucinationAggregationJob>();

// ── EP-013 Verification Enforcement (US_075, AC-1–AC-4, AIR-S02, AIR-S03) ───────────────────
// IVerificationEnforcementService: Scoped — approve, modify, reject single and batch AI outputs;
//   records CodingAuditLog entries (old/new values + justification) for MedicalCode operations
//   and AuditLog entries for ExtractedData operations; blocks low-confidence data from profile
//   consolidation until verified (AC-1, AC-2, AC-3).
// VerificationRequiredFilter: Scoped ServiceFilter — rejects HTTP requests attempting to
//   finalize unverified MedicalCode or ExtractedData records with HTTP 400 "verification_required"
//   (AC-4). Apply with [ServiceFilter(typeof(VerificationRequiredFilter))] on finalization endpoints.
// By design: NO auto-approval mechanism — PendingVerification status persists indefinitely
//   until staff action per AIR-S03 compliance (edge case: no staff available).
builder.Services.AddScoped<UPACIP.Service.Verification.IVerificationEnforcementService, UPACIP.Service.Verification.VerificationEnforcementService>();
builder.Services.AddScoped<UPACIP.Api.Filters.VerificationRequiredFilter>();

// ── EP-014 Document Chunking (US_076, AC-1, AIR-R01) ─────────────────────────────────────────
// TiktokenTokenizer (singleton): cl100k_base BPE encoding compatible with OpenAI
//   text-embedding-3-small. Thread-safe — shared across all requests.
//   NOTE: first call to CreateForEncoding downloads the vocabulary from the tiktoken CDN and
//   caches it in the local NuGet package folder; subsequent restarts use the cached file.
// IDocumentChunkingService (singleton): deterministic sliding-window chunking —
//   512-token window, 410-token step (102-token / ~20% overlap per AIR-R01).
//   Short documents (<100 tokens) are returned as a single chunk (edge case).
//   Tables are converted to structured text; images replaced with [image-content-excluded].
//   Output validation: logs warnings on data-loss or chunks exceeding 520-token soft limit.
builder.Services.AddSingleton<Microsoft.ML.Tokenizers.TiktokenTokenizer>(_ =>
    Microsoft.ML.Tokenizers.TiktokenTokenizer.CreateForEncoding("cl100k_base"));
builder.Services.AddSingleton<UPACIP.Service.Rag.Chunking.IDocumentChunkingService,
    UPACIP.Service.Rag.Chunking.DocumentChunkingService>();

// ── EP-014 Embedding Generation (US_076, AC-2, AC-3, AC-4, AIR-R04, AIR-O06, AIR-O08) ───────
// IEmbeddingGenerationService (scoped): depends on scoped IVectorSearchService so it must be
//   scoped. Wraps OpenAI text-embedding-3-small calls through the "openai" named HttpClient
//   (circuit breaker + base URL pre-configured above). Redis embedding cache uses 24-hour TTL
//   keyed by SHA-256(text) preventing PII in cache keys (AIR-O06, OWASP A02).
//   Polly V8 retry: 3 retries, exponential backoff with jitter (AIR-O08).
// DocumentIngestionWorker (singleton BackgroundService): FIFO Redis queue consumer
//   (queue:document-ingestion). Creates one DI scope per job. Circuit breaker opens after
//   5 consecutive job failures; half-open after 30 s (AIR-O04). Job-level retry: up to 3
//   re-queues before routing to dead-letter queue (queue:document-ingestion:dead).
builder.Services.AddScoped<UPACIP.Service.Rag.Embedding.IEmbeddingGenerationService,
    UPACIP.Service.Rag.Embedding.EmbeddingGenerationService>();
builder.Services.AddHostedService<UPACIP.Service.Rag.Embedding.DocumentIngestionWorker>();

// ── EP-014 RAG Retrieval (US_077, AC-1, AC-2, AIR-R02) ───────────────────────────────────────
// IHybridSearchOrchestrator (scoped, US_078): parallel hybrid (vector + FTS) search across
//   one or all embedding category indexes. Deduplicates by chunk Id, applies configurable
//   weighted scoring (SemanticWeight + KeywordWeight == 1.0 validated at startup), and boosts
//   exact whole-word query matches by ExactMatchBoostFactor (default 2.0) per US_078 edge case.
//   Category-scoped: single EmbeddingCategory → queries only that index (AC-4).
//   Options bound from appsettings.json "HybridSearch" section.
// IRagRetrievalService (scoped): delegates to IHybridSearchOrchestrator when
//   RetrievalRequest.UseHybridSearch=true; falls back to direct cosine-similarity otherwise.
//   Result cache: 5-minute TTL keyed by SHA-256(embedding + categories) via ICacheService
//   (NFR-030, AIR-O06). Sets IsGrounded=false / GroundingStatus="no-grounding-available" when
//   no chunks meet the threshold.
builder.Services.Configure<UPACIP.Service.Rag.Models.HybridSearchOptions>(
    builder.Configuration.GetSection(UPACIP.Service.Rag.Models.HybridSearchOptions.SectionName));
builder.Services.AddScoped<UPACIP.Service.Rag.IHybridSearchOrchestrator,
    UPACIP.Service.Rag.HybridSearchOrchestrator>();
builder.Services.AddScoped<UPACIP.Service.Rag.IRagRetrievalService,
    UPACIP.Service.Rag.RagRetrievalService>();

// ── EP-014 Semantic Re-ranking + Context Building (US_077, AC-3, AC-4, AIR-R02, AIR-R03) ─────
// ISemanticReranker (scoped): LLM-based relevance scoring via "openai" named HttpClient
//   (primary: GPT-4o-mini, fallback: Anthropic Claude 3.5 Sonnet). Per-instance Polly V7
//   circuit breaker (3 failures → open 30s, AIR-O04). Token budget: 500 input / 200 output
//   tokens (AIR-O01). Domain priority weights resolve ambiguous multi-domain queries
//   (MedicalTerminology > IntakeTemplate > CodingGuideline). On AI Gateway failure, falls
//   back to cosine-similarity ordering (UsedLlmReranking=false). PII never logged (AIR-S04).
//   Prompt template: Rag/Prompts/reranking-prompt.liquid (inline fallback).
// IRagContextBuilder (scoped): stateless formatter — converts RankedChunks into
//   [GROUNDING CONTEXT]...[/GROUNDING CONTEXT] numbered citation blocks for prompt injection
//   (AC-4). Produces GroundingStatus="no-grounding-available" when chunks list is empty.
builder.Services.AddScoped<UPACIP.Service.Rag.ISemanticReranker,
    UPACIP.Service.Rag.SemanticReranker>();
builder.Services.AddScoped<UPACIP.Service.Rag.IRagContextBuilder,
    UPACIP.Service.Rag.RagContextBuilder>();

// ── EP-014 Knowledge-Base Refresh (US_078 AC-3, AIR-R05) ────────────────────────────────────
// IKnowledgeBaseRefreshService (scoped): admin-triggered quarterly refresh pipeline.
//   Diffs incoming code entries against live pgvector tables, chunks + embeds new/updated
//   descriptions via IDocumentChunkingService + IEmbeddingGenerationService, writes to
//   staging tables, then executes an atomic DDL swap (live→old, staging→live) so in-flight
//   queries always see a consistent index (US_078 edge case: mid-refresh query safety).
//   Deprecated codes are soft-marked with deprecated_at timestamp (AC-3).
//   Index rebuild (REINDEX CONCURRENTLY) runs outside the swap transaction (PG16 constraint).
//   On any failure, live table is untouched and RefreshResult.Status = Failed (AIR-R05).
//   PII guard (AIR-S04): only admin user ID, version, and counts are logged — no code text.
builder.Services.AddScoped<UPACIP.Service.Rag.Refresh.IKnowledgeBaseRefreshService,
    UPACIP.Service.Rag.Refresh.KnowledgeBaseRefreshService>();

// ── EP-012 AI Cost Monitoring (US_071 TASK_002) ──────────────────────────────────────────────
// AiCostAggregationService: Scoped — reads AiRequestLog rows for the target day, recalculates
//   approximate costs from the AiCostBudgetConfig rate card, and upserts AiCostDailySummary
//   rows (US_071 AC-1, edge case: approximate cost flagging).
// AiCostAlertService: Scoped — compares daily totals per provider against DailyBudgetThreshold;
//   emits LogCritical structured events when AlertEnabled=true and the threshold is exceeded
//   (US_071 AC-2).
// AiCostAggregationJob: BackgroundService — fires every 24 h (runs once on startup as well);
//   creates a fresh IServiceScope per execution so scoped services are isolated and disposed.
//   Retry: up to 3 attempts with exponential backoff (5 s, 25 s, 125 s) per NFR-032.
builder.Services.AddScoped<UPACIP.Service.AI.AiCost.IAiCostAggregationService, UPACIP.Service.AI.AiCost.AiCostAggregationService>();
builder.Services.AddScoped<UPACIP.Service.AI.AiCost.IAiCostAlertService, UPACIP.Service.AI.AiCost.AiCostAlertService>();
builder.Services.AddHostedService<UPACIP.Service.AI.AiCost.AiCostAggregationJob>();

// ── Payer rule validation (US_051, AC-1, AC-2, AC-3, AC-4, FR-066) ─────────────────────────
// IPayerRuleValidationService: Scoped — validates code combinations against payer-specific
//   and CMS-default rules, detects denial risks, and validates NCCI bundling edits.
//   Uses Redis cache (5-minute TTL per NFR-030) for payer rule sets.
// IMultiCodeAssignmentService: Scoped — assigns multiple codes with individual verification
//   and billing priority ordering. Writes CodingAuditLog entries for HIPAA compliance.
builder.Services.AddScoped<UPACIP.Service.Coding.IPayerRuleValidationService, UPACIP.Service.Coding.PayerRuleValidationService>();
builder.Services.AddScoped<UPACIP.Service.Coding.IMultiCodeAssignmentService, UPACIP.Service.Coding.MultiCodeAssignmentService>();

// ── Arrival Queue Service (US_052, US_053, AC-1 through AC-4) ───────────────────────────────
// IQueueService: Scoped — marks arrivals, updates status, overrides no-shows, bulk no-show detection.
//   Depends on ApplicationDbContext (Scoped), ICacheService (Singleton), IQueueCacheService (Singleton).
//   Cache key: "queue:today:{date:yyyyMMdd}", TTL 5 min (NFR-030, NFR-004).
// IQueueCacheService: Singleton — per-filter granular cache keys for the US_053 dashboard.
//   Uses IConnectionMultiplexer (StackExchange.Redis) for SCAN-based pattern invalidation.
// NoShowDetectionService: Singleton BackgroundService — polls every 60 seconds to mark no-shows
//   (AC-2). Creates a fresh DI scope per cycle to resolve IQueueService. Exposes IHealthCheck
//   that degrades after 3 consecutive failures.
// QueueSettings: configurable wait time threshold (default 30 min) for alert count.
builder.Services.Configure<UPACIP.Service.Queue.QueueSettings>(
    builder.Configuration.GetSection(UPACIP.Service.Queue.QueueSettings.SectionName));
builder.Services.AddSingleton<UPACIP.Service.Caching.IQueueCacheService, UPACIP.Service.Caching.QueueCacheService>();
builder.Services.AddScoped<UPACIP.Service.Queue.IQueueService, UPACIP.Service.Queue.QueueService>();
builder.Services.AddHostedService<UPACIP.Service.Queue.NoShowDetectionService>();
builder.Services.AddScoped<UPACIP.Service.Dashboard.IStaffDashboardService, UPACIP.Service.Dashboard.StaffDashboardService>();
builder.Services.AddScoped<UPACIP.Service.Admin.ISystemMetricsService,  UPACIP.Service.Admin.SystemMetricsService>();
builder.Services.AddScoped<UPACIP.Service.Admin.IConfigurationService,  UPACIP.Service.Admin.ConfigurationService>();
builder.Services.AddScoped<UPACIP.Service.Admin.IAdminUserService,       UPACIP.Service.Admin.AdminUserService>();
builder.Services.AddScoped<UPACIP.Service.Admin.ISlotTemplateService,            UPACIP.Service.Admin.SlotTemplateService>();
builder.Services.AddScoped<UPACIP.Service.Admin.IBusinessHoursService,           UPACIP.Service.Admin.BusinessHoursService>();
builder.Services.AddScoped<UPACIP.Service.Admin.INotificationTemplateService,    UPACIP.Service.Admin.NotificationTemplateService>();
builder.Services.AddScoped<UPACIP.Service.Admin.IRiskConfigService,              UPACIP.Service.Admin.RiskConfigService>();

// ASP.NET Core built-in rate limiting (Microsoft.AspNetCore.RateLimiting — included in .NET 7+).
// Policy "check-email-limit": 30 req/min per IP — anti-enumeration guard (OWASP A07).
// The resend-verification endpoint uses application-level Redis rate limiting inside
// RegistrationService for per-email granularity (edge case: max 3 per 5 min per email).
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("check-email-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit      = 30;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    // Generic registration limiter: 10 registrations per minute per IP.
    options.AddFixedWindowLimiter("register-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit      = 10;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    // Forgot-password limiter: max 5 requests per 15 minutes per IP (US_015 abuse prevention).
    options.AddFixedWindowLimiter("forgot-password-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromMinutes(15);
        limiterOptions.PermitLimit      = 5;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    // MFA verify limiter: 5 attempts per minute per IP — prevents TOTP brute force (US_016 AC-1).
    options.AddFixedWindowLimiter("mfa-verify-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit      = 5;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    // ICD-10 generation limiter: 100 requests per hour per user (AIR-S08, US_047).
    // Token consumption is LLM-bound; a generous but bounded window prevents runaway
    // batch calls from exhausting the AI provider token budget.
    options.AddFixedWindowLimiter("icd10-generate-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromHours(1);
        limiterOptions.PermitLimit      = 100;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    // CPT generation limiter: 100 requests per hour (mirrors icd10-generate-limit, AIR-S08, US_048).
    options.AddFixedWindowLimiter("cpt-generate-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromHours(1);
        limiterOptions.PermitLimit      = 100;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    // Session time-remaining limiter: 6 requests per minute per IP (≈1 per 10 s).
    // Prevents clients from polling /api/session/time-remaining too aggressively (US_065 AC-4).
    options.AddFixedWindowLimiter("session-time-remaining-limit", limiterOptions =>
    {
        limiterOptions.Window           = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit      = 6;
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit       = 0;
    });

    options.RejectionStatusCode = 429;
});

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

// HTTPS redirect — 301 Permanent (AC-4).  ASP.NET Core 8 default is 307 (temporary);
// a permanent redirect is required so browsers and bots cache the upgrade and stop
// sending plain HTTP requests (OWASP A02, NFR-010, FR-092).
// HttpsPort is read from Kestrel:Endpoints:Https or ASPNETCORE_HTTPS_PORT.
builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = StatusCodes.Status301MovedPermanently;
    options.HttpsPort = builder.Configuration.GetValue<int?>("Kestrel:Endpoints:Https:Port")
                        ?? 443;
});

// HSTS — HTTP Strict Transport Security (AC-3, NFR-010).
// MaxAge 365 days, IncludeSubDomains, Preload — meets browser preload-list requirements.
// Only applied outside Development so the local dev workflow using HTTP is unaffected.
builder.Services.AddHsts(options =>
{
    var maxAgeDays = builder.Configuration.GetValue<int>("Security:Tls:HstsMaxAgeDays", defaultValue: 365);
    options.MaxAge           = TimeSpan.FromDays(maxAgeDays);
    options.IncludeSubDomains = true;
    options.Preload           = true;
});

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
        timeout: TimeSpan.FromSeconds(3))
    // TLS certificate expiry monitor — Degraded when < CertExpiryWarningDays remain,
    // Unhealthy when expired or unreachable (US_063 AC-3, edge case: cert expiry alert).
    .AddCheck<TlsCertificateHealthCheck>(
        name: "tls-certificate",
        tags: new[] { "ready" })
    // Audit failover queue depth monitor — Degraded when 1-100 entries pending,
    // Unhealthy when > 100 entries pending (US_064 edge case, NFR-032).
    .AddCheck<AuditQueueHealthCheck>(
        name: "audit-queue",
        tags: new[] { "audit" });

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
// HSTS — inject Strict-Transport-Security header on HTTPS responses (AC-3).
// Skipped in Development so local HTTP tooling is unaffected.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseCors("ReactFrontend");
app.UseRateLimiter();        // Rate limiting policies (register-limit, check-email-limit)
app.UseAuthentication(); // Must precede UseAuthorization to populate HttpContext.User
app.UseSessionManagement(); // Sliding 15-min TTL reset + expired session 401 (NFR-014, AC-1/AC-2)
// Input sanitization: XSS + command injection stripping on all JSON bodies and query strings.
// Placed after authentication so the correlation ID is available for warning logs.
// Placed before UseAuthorization so policy checks operate on sanitized data (US_066 AC-1, FR-095).
app.UseInputSanitization();
app.UseAuthorization();
app.MapControllers();

// ── AI Gateway admin endpoints — model version management (US_069 TASK_003, AIR-O05) ──
// Requires AdminOnly authorization policy (set in RequireAuthorization inside MapModelVersionEndpoints).
app.MapModelVersionEndpoints();

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

