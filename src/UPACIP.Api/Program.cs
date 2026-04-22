using FluentValidation;
using FluentValidation.AspNetCore;
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

// ── EP-005 booking confirmation orchestration (US_034 task_001_be_booking_confirmation_notification_orchestration) ──
// StubPdfConfirmationService is a compile-safe stub — replaced when task_002_be_pdf_confirmation_and_qr_generation
// delivers the real PDF/QR pipeline.  The orchestration service always sends email without PDF
// (EC-1 path) until the real implementation is registered.
// BookingConfirmationNotificationService is Scoped — it depends on ApplicationDbContext, email/SMS
// services, and always uses CancellationToken.None (decoupled from the booking request lifetime).
builder.Services.AddScoped<IPdfConfirmationService, StubPdfConfirmationService>();
builder.Services.AddScoped<IBookingConfirmationNotificationService, BookingConfirmationNotificationService>();
builder.Services.AddScoped<IPasswordValidator<ApplicationUser>, PasswordComplexityValidator>();

// Password reset service — token generation, validation, post-reset session cleanup (US_015).
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// MFA service — TOTP secret generation, AES-256 encryption, backup code hashing (US_016 AC-1).
builder.Services.AddScoped<IMfaService, MfaService>();

// Audit log service — append-only auth event logging (US_016 AC-5).
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

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
builder.Services.AddSingleton<WaitlistOfferProcessor>();
builder.Services.AddSingleton<IWaitlistOfferQueue>(sp => sp.GetRequiredService<WaitlistOfferProcessor>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<WaitlistOfferProcessor>());
builder.Services.AddScoped<IWaitlistService, WaitlistService>();

// Preferred-slot swap engine (US_021) — evaluates freed slots against waiting patients'
// preferred criteria and auto-swaps or sends manual confirmation offers.
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
app.UseRateLimiter();        // Rate limiting policies (register-limit, check-email-limit)
app.UseAuthentication(); // Must precede UseAuthorization to populate HttpContext.User
app.UseSessionManagement(); // Sliding 15-min TTL reset + expired session 401 (NFR-014, AC-1/AC-2)
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

