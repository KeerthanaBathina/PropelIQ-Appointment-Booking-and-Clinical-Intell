# Task - task_001_be_idempotency_middleware

## Requirement Reference

- User Story: us_102
- Story Location: .propel/context/tasks/EP-020/us_102/us_102.md
- Acceptance Criteria:
  - AC-1: Given a state-changing API endpoint (POST, PUT, DELETE) exists, When the same request is sent multiple times with an idempotency key, Then only the first request modifies state; subsequent requests return the cached result.
  - AC-3: Given the API is documented, When a developer accesses the OpenAPI 3.0 spec, Then all endpoints, request/response schemas, error codes, and example payloads are generated from the code.
- Edge Case:
  - What happens when an idempotency key is reused with different request body? System rejects the request with HTTP 422 Unprocessable Entity explaining the key mismatch.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Backend | ASP.NET Core MVC | 8.x |
| Caching | Upstash Redis | 7.x |
| Validation | FluentValidation.AspNetCore | 11.x |
| Documentation | Swagger (Swashbuckle) | 6.x |
| Monitoring | Serilog | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement an idempotency middleware for all state-changing API endpoints (POST, PUT, DELETE) using Redis as the idempotency key cache store. Clients include an `Idempotency-Key` header (UUID v4) on mutating requests. The middleware intercepts the request, checks Redis for an existing entry under that key, and either returns the cached response (replay) or allows the request to proceed and caches the result. If a key is reused with a different request body hash, the middleware rejects with HTTP 422 (edge case 1). The `[IdempotentEndpoint]` attribute marks controllers/actions that require idempotency enforcement, and the `Idempotency-Key` header is documented in Swagger via an `IOperationFilter` (AC-3 partial). Cached results expire after a configurable TTL (default 24 hours) to prevent unbounded Redis growth.

## Dependent Tasks

- US_001 — Requires backend API scaffold with middleware pipeline.
- US_004 — Requires Redis connection and caching infrastructure.

## Impacted Components

- **CREATE** `src/UPACIP.Api/Middleware/IdempotencyMiddleware.cs` — Middleware: intercepts mutating requests, checks/stores in Redis
- **CREATE** `src/UPACIP.Api/Attributes/IdempotentEndpointAttribute.cs` — Marker attribute for idempotent endpoints
- **CREATE** `src/UPACIP.Service/Idempotency/IIdempotencyStore.cs` — Interface: key lookup, store, body hash comparison
- **CREATE** `src/UPACIP.Service/Idempotency/RedisIdempotencyStore.cs` — Redis-backed implementation with TTL
- **CREATE** `src/UPACIP.Service/Idempotency/IdempotencyRecord.cs` — Model: key, body hash, status code, response body, created
- **CREATE** `src/UPACIP.Service/Idempotency/IdempotencyOptions.cs` — Configurable TTL and key header name
- **CREATE** `src/UPACIP.Api/Swagger/IdempotencyKeyOperationFilter.cs` — Swagger filter to document Idempotency-Key header
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register idempotency middleware, store, and Swagger filter

## Implementation Plan

1. **Define `IdempotencyRecord` model**: Create `src/UPACIP.Service/Idempotency/IdempotencyRecord.cs`:
   ```csharp
   public sealed class IdempotencyRecord
   {
       public required string Key { get; init; }
       public required string RequestBodyHash { get; init; }
       public int StatusCode { get; set; }
       public string? ResponseBody { get; set; }
       public Dictionary<string, string> ResponseHeaders { get; set; } = new();
       public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
       public bool IsCompleted { get; set; }
   }
   ```
   - `RequestBodyHash` stores a SHA-256 hash of the request body — used to detect key reuse with a different payload (edge case 1).
   - `IsCompleted` distinguishes in-flight requests (processing) from completed requests (cacheable). Prevents concurrent duplicate submissions from both executing.
   - `ResponseHeaders` captures content-type and custom headers for faithful replay.

2. **Define `IdempotencyOptions`**: Create `src/UPACIP.Service/Idempotency/IdempotencyOptions.cs`:
   ```csharp
   public sealed class IdempotencyOptions
   {
       public const string SectionName = "Idempotency";

       public string HeaderName { get; init; } = "Idempotency-Key";
       public int TtlHours { get; init; } = 24;
       public int MaxBodySizeBytes { get; init; } = 1_048_576; // 1MB
   }
   ```
   - `TtlHours` default of 24 hours — balances cache utility with Redis memory usage.
   - `MaxBodySizeBytes` — prevents caching responses larger than 1MB to avoid Redis memory pressure.
   - `HeaderName` is configurable but defaults to the standard `Idempotency-Key` header name.

3. **Define `IIdempotencyStore` interface**: Create `src/UPACIP.Service/Idempotency/IIdempotencyStore.cs`:
   ```csharp
   public interface IIdempotencyStore
   {
       Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default);
       Task<bool> TryCreateAsync(IdempotencyRecord record, CancellationToken ct = default);
       Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default);
       Task RemoveAsync(string key, CancellationToken ct = default);
   }
   ```
   - `TryCreateAsync` returns `false` if the key already exists (atomic SET NX in Redis) — prevents race conditions between concurrent duplicate requests.
   - `RemoveAsync` used for cleanup when the original request fails with a non-retryable error.

4. **Implement `RedisIdempotencyStore` (AC-1)**: Create `src/UPACIP.Service/Idempotency/RedisIdempotencyStore.cs`:
   ```csharp
   public sealed class RedisIdempotencyStore : IIdempotencyStore
   {
       private readonly IConnectionMultiplexer _redis;
       private readonly IdempotencyOptions _options;
       private readonly ILogger<RedisIdempotencyStore> _logger;
       private const string KeyPrefix = "idempotency:";

       public RedisIdempotencyStore(
           IConnectionMultiplexer redis,
           IOptions<IdempotencyOptions> options,
           ILogger<RedisIdempotencyStore> logger)
       {
           _redis = redis;
           _options = options.Value;
           _logger = logger;
       }

       public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default)
       {
           var db = _redis.GetDatabase();
           var value = await db.StringGetAsync($"{KeyPrefix}{key}");

           if (value.IsNullOrEmpty)
               return null;

           return JsonSerializer.Deserialize<IdempotencyRecord>(value!);
       }

       public async Task<bool> TryCreateAsync(IdempotencyRecord record, CancellationToken ct = default)
       {
           var db = _redis.GetDatabase();
           var serialized = JsonSerializer.Serialize(record);
           var ttl = TimeSpan.FromHours(_options.TtlHours);

           // SET NX — only set if key does not exist (atomic)
           var created = await db.StringSetAsync(
               $"{KeyPrefix}{record.Key}",
               serialized,
               ttl,
               When.NotExists);

           if (created)
           {
               _logger.LogDebug(
                   "IDEMPOTENCY_KEY_CREATED: Key={Key}, TTL={TtlHours}h",
                   record.Key, _options.TtlHours);
           }

           return created;
       }

       public async Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default)
       {
           var db = _redis.GetDatabase();
           var serialized = JsonSerializer.Serialize(record);
           var ttl = TimeSpan.FromHours(_options.TtlHours);

           await db.StringSetAsync(
               $"{KeyPrefix}{record.Key}",
               serialized,
               ttl,
               When.Exists); // Only update existing keys

           _logger.LogDebug(
               "IDEMPOTENCY_KEY_UPDATED: Key={Key}, StatusCode={StatusCode}, Completed={IsCompleted}",
               record.Key, record.StatusCode, record.IsCompleted);
       }

       public async Task RemoveAsync(string key, CancellationToken ct = default)
       {
           var db = _redis.GetDatabase();
           await db.KeyDeleteAsync($"{KeyPrefix}{key}");
       }
   }
   ```
   Key behaviors:
   - **Atomic creation** via `SET NX` — if two identical requests arrive simultaneously, only one succeeds in creating the record. The second sees the existing record and waits or replays.
   - **TTL enforcement** — every key expires after `TtlHours` (default 24h). No manual cleanup required.
   - **`KeyPrefix`** scopes idempotency keys within Redis, preventing collisions with other cached data.
   - **`When.Exists`** on update — prevents re-creating an expired/deleted key during the response phase.

5. **Create `IdempotentEndpointAttribute`**: Create `src/UPACIP.Api/Attributes/IdempotentEndpointAttribute.cs`:
   ```csharp
   [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
   public sealed class IdempotentEndpointAttribute : Attribute
   {
       /// <summary>
       /// Whether the Idempotency-Key header is required. Default: true for POST, false for PUT/DELETE.
       /// </summary>
       public bool Required { get; init; } = true;
   }
   ```
   - Applied to controller actions: `[IdempotentEndpoint]` on POST endpoints, `[IdempotentEndpoint(Required = false)]` on PUT/DELETE (PUT/DELETE are naturally idempotent by HTTP semantics but can opt in for response caching).
   - Class-level application marks all actions in a controller.

6. **Implement `IdempotencyMiddleware` (AC-1, edge case 1)**: Create `src/UPACIP.Api/Middleware/IdempotencyMiddleware.cs`:
   ```csharp
   public sealed class IdempotencyMiddleware
   {
       private readonly RequestDelegate _next;
       private readonly ILogger<IdempotencyMiddleware> _logger;
       private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
       {
           "POST", "PUT", "DELETE", "PATCH"
       };

       public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
       {
           _next = next;
           _logger = logger;
       }

       public async Task InvokeAsync(
           HttpContext context,
           IIdempotencyStore store,
           IOptions<IdempotencyOptions> options)
       {
           // Skip non-mutating methods
           if (!MutatingMethods.Contains(context.Request.Method))
           {
               await _next(context);
               return;
           }

           // Check for [IdempotentEndpoint] attribute
           var endpoint = context.GetEndpoint();
           var attribute = endpoint?.Metadata.GetMetadata<IdempotentEndpointAttribute>();
           if (attribute is null)
           {
               await _next(context);
               return;
           }

           var opts = options.Value;
           var idempotencyKey = context.Request.Headers[opts.HeaderName].FirstOrDefault();

           // If header missing and required, return 400
           if (string.IsNullOrWhiteSpace(idempotencyKey))
           {
               if (attribute.Required)
               {
                   context.Response.StatusCode = StatusCodes.Status400BadRequest;
                   await context.Response.WriteAsJsonAsync(new
                   {
                       error = "Missing required Idempotency-Key header",
                       detail = $"Include a UUID v4 value in the '{opts.HeaderName}' header"
                   });
                   return;
               }

               await _next(context);
               return;
           }

           // Validate UUID format
           if (!Guid.TryParse(idempotencyKey, out _))
           {
               context.Response.StatusCode = StatusCodes.Status400BadRequest;
               await context.Response.WriteAsJsonAsync(new
               {
                   error = "Invalid Idempotency-Key format",
                   detail = "Idempotency-Key must be a valid UUID v4"
               });
               return;
           }

           // Compute request body hash for mismatch detection
           context.Request.EnableBuffering();
           var bodyBytes = await ReadRequestBodyAsync(context.Request);
           var bodyHash = ComputeHash(bodyBytes);
           context.Request.Body.Position = 0;

           // Check for existing record
           var existing = await store.GetAsync(idempotencyKey);

           if (existing is not null)
           {
               // Edge case: key reused with different body → 422
               if (existing.RequestBodyHash != bodyHash)
               {
                   _logger.LogWarning(
                       "IDEMPOTENCY_KEY_MISMATCH: Key={Key}, ExpectedHash={ExpectedHash}, " +
                       "ReceivedHash={ReceivedHash}",
                       idempotencyKey, existing.RequestBodyHash, bodyHash);

                   context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                   await context.Response.WriteAsJsonAsync(new
                   {
                       error = "Idempotency key mismatch",
                       detail = "This idempotency key was used with a different request body. " +
                                "Use a new key for different requests."
                   });
                   return;
               }

               // If completed, replay cached response
               if (existing.IsCompleted)
               {
                   _logger.LogInformation(
                       "IDEMPOTENCY_REPLAY: Key={Key}, CachedStatusCode={StatusCode}",
                       idempotencyKey, existing.StatusCode);

                   context.Response.StatusCode = existing.StatusCode;
                   context.Response.Headers.Append("Idempotency-Replayed", "true");

                   foreach (var header in existing.ResponseHeaders)
                   {
                       context.Response.Headers[header.Key] = header.Value;
                   }

                   if (existing.ResponseBody is not null)
                   {
                       context.Response.ContentType = "application/json";
                       await context.Response.WriteAsync(existing.ResponseBody);
                   }

                   return;
               }

               // In-flight: another request is processing — return 409 Conflict
               context.Response.StatusCode = StatusCodes.Status409Conflict;
               await context.Response.WriteAsJsonAsync(new
               {
                   error = "Request in progress",
                   detail = "A request with this idempotency key is currently being processed."
               });
               return;
           }

           // Create new record (in-flight)
           var record = new IdempotencyRecord
           {
               Key = idempotencyKey,
               RequestBodyHash = bodyHash,
               IsCompleted = false,
           };

           var created = await store.TryCreateAsync(record);
           if (!created)
           {
               // Lost race — another request created the key first
               context.Response.StatusCode = StatusCodes.Status409Conflict;
               await context.Response.WriteAsJsonAsync(new
               {
                   error = "Request in progress",
                   detail = "A request with this idempotency key is currently being processed."
               });
               return;
           }

           // Capture response
           var originalBody = context.Response.Body;
           using var memoryStream = new MemoryStream();
           context.Response.Body = memoryStream;

           try
           {
               await _next(context);

               // Cache the response
               memoryStream.Position = 0;
               var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();

               record.StatusCode = context.Response.StatusCode;
               record.ResponseBody = responseBody.Length <= options.Value.MaxBodySizeBytes
                   ? responseBody : null;
               record.IsCompleted = true;
               record.ResponseHeaders["Content-Type"] =
                   context.Response.ContentType ?? "application/json";

               await store.UpdateAsync(record);

               // Write response to original stream
               memoryStream.Position = 0;
               await memoryStream.CopyToAsync(originalBody);
           }
           catch (Exception)
           {
               // Clean up failed request so the key can be retried
               await store.RemoveAsync(idempotencyKey);
               throw;
           }
           finally
           {
               context.Response.Body = originalBody;
           }
       }

       private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request)
       {
           using var ms = new MemoryStream();
           await request.Body.CopyToAsync(ms);
           return ms.ToArray();
       }

       private static string ComputeHash(byte[] data)
       {
           var hashBytes = SHA256.HashData(data);
           return Convert.ToHexString(hashBytes);
       }
   }
   ```
   Key behaviors:
   - **AC-1 (idempotent state changes)**: First request creates an in-flight record via atomic `SET NX`, processes the request, then caches the response. Subsequent requests with the same key return the cached response with an `Idempotency-Replayed: true` header.
   - **Edge case 1 (key reuse with different body)**: SHA-256 hash of the request body is stored with the key. If a subsequent request uses the same key but produces a different hash, 422 is returned with an explanation.
   - **Concurrent duplicate protection**: `TryCreateAsync` uses Redis `SET NX` (atomic). If two identical requests arrive simultaneously, only one creates the record. The second sees a 409 Conflict indicating in-flight processing.
   - **Failure cleanup**: If the downstream handler throws, the idempotency record is removed so the client can retry with the same key.
   - **Response size limit**: Responses larger than `MaxBodySizeBytes` (1MB) are not cached — the request is still idempotent (state change happens once) but the response is not replayed.

7. **Create `IdempotencyKeyOperationFilter` for Swagger (AC-3)**: Create `src/UPACIP.Api/Swagger/IdempotencyKeyOperationFilter.cs`:
   ```csharp
   public sealed class IdempotencyKeyOperationFilter : IOperationFilter
   {
       public void Apply(OpenApiOperation operation, OperationFilterContext context)
       {
           var attribute = context.MethodInfo
               .GetCustomAttribute<IdempotentEndpointAttribute>()
               ?? context.MethodInfo.DeclaringType?
                   .GetCustomAttribute<IdempotentEndpointAttribute>();

           if (attribute is null)
               return;

           operation.Parameters ??= new List<OpenApiParameter>();
           operation.Parameters.Add(new OpenApiParameter
           {
               Name = "Idempotency-Key",
               In = ParameterLocation.Header,
               Required = attribute.Required,
               Description = "UUID v4 idempotency key. Same key with same body returns cached result. " +
                             "Same key with different body returns 422.",
               Schema = new OpenApiSchema
               {
                   Type = "string",
                   Format = "uuid",
                   Example = new OpenApiString("550e8400-e29b-41d4-a716-446655440000")
               }
           });

           // Document 422 response for key mismatch
           operation.Responses.TryAdd("422", new OpenApiResponse
           {
               Description = "Idempotency key reused with different request body"
           });

           // Document 409 response for in-flight
           operation.Responses.TryAdd("409", new OpenApiResponse
           {
               Description = "Request with this idempotency key is currently being processed"
           });
       }
   }
   ```
   - Automatically documents the `Idempotency-Key` header on all endpoints with `[IdempotentEndpoint]`.
   - Includes 422 and 409 error responses in the Swagger spec.
   - Example UUID in the schema helps API consumers understand the expected format.

8. **Register middleware, store, and Swagger filter**: Update `Program.cs`:
   ```csharp
   // Configuration
   builder.Services.Configure<IdempotencyOptions>(
       builder.Configuration.GetSection(IdempotencyOptions.SectionName));

   // Services
   builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

   // Swagger filter (add to existing Swashbuckle config)
   builder.Services.AddSwaggerGen(c =>
   {
       c.OperationFilter<IdempotencyKeyOperationFilter>();
   });

   // Middleware (after authentication, before endpoint routing)
   app.UseMiddleware<IdempotencyMiddleware>();
   ```
   Add to `appsettings.json`:
   ```json
   {
     "Idempotency": {
       "HeaderName": "Idempotency-Key",
       "TtlHours": 24,
       "MaxBodySizeBytes": 1048576
     }
   }
   ```

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Attributes/                          ← new directory
│   │   ├── Configuration/
│   │   ├── Controllers/
│   │   ├── HealthChecks/
│   │   ├── Middleware/
│   │   ├── Swagger/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Configuration/
│   │   ├── FeatureFlags/
│   │   └── Idempotency/                         ← new directory
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (backend API scaffold) and US_004 (Redis infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Idempotency/IdempotencyRecord.cs | Model: key, body hash, status code, response body, completion flag |
| CREATE | src/UPACIP.Service/Idempotency/IdempotencyOptions.cs | Configurable TTL (24h), header name, max body size |
| CREATE | src/UPACIP.Service/Idempotency/IIdempotencyStore.cs | Interface: Get, TryCreate (atomic), Update, Remove |
| CREATE | src/UPACIP.Service/Idempotency/RedisIdempotencyStore.cs | Redis SET NX implementation with TTL and key prefix |
| CREATE | src/UPACIP.Api/Attributes/IdempotentEndpointAttribute.cs | Marker attribute with Required property |
| CREATE | src/UPACIP.Api/Middleware/IdempotencyMiddleware.cs | Request interception, hash comparison, response caching |
| CREATE | src/UPACIP.Api/Swagger/IdempotencyKeyOperationFilter.cs | Swagger: Idempotency-Key header + 422/409 responses |
| MODIFY | src/UPACIP.Api/Program.cs | Register idempotency store, middleware, Swagger filter |

## External References

- [IETF — Idempotency-Key Header (draft)](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/)
- [Stripe — Idempotent Requests](https://stripe.com/docs/api/idempotent_requests)
- [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
- [StackExchange.Redis — StringSetAsync with When.NotExists](https://stackexchange.github.io/StackExchange.Redis/Basics.html)
- [Swashbuckle — IOperationFilter](https://github.com/domaindrivendev/Swashbuckle.AspNetCore#operation-filters)
- [SHA-256 in .NET 8](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdata)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] POST with Idempotency-Key: first request processes, second returns cached result (AC-1)
- [ ] PUT/DELETE with Idempotency-Key: same cached replay behavior (AC-1)
- [ ] Same key + different body → HTTP 422 with mismatch explanation (edge case 1)
- [ ] Missing required Idempotency-Key header → HTTP 400
- [ ] Invalid UUID format → HTTP 400
- [ ] Concurrent duplicate requests → only one processes, other gets 409 (atomic SET NX)
- [ ] Failed request → idempotency record removed, key retryable
- [ ] Swagger shows Idempotency-Key header, 422, 409 responses on marked endpoints (AC-3)
- [ ] Redis key expires after configured TTL (default 24h)

## Implementation Checklist

- [ ] Create IdempotencyRecord model with key, body hash, status code, response body
- [ ] Create IdempotencyOptions with configurable TTL, header name, max body size
- [ ] Define IIdempotencyStore interface with atomic TryCreate
- [ ] Implement RedisIdempotencyStore with SET NX, TTL, key prefix
- [ ] Create IdempotentEndpointAttribute with Required property
- [ ] Implement IdempotencyMiddleware with hash comparison and response caching
- [ ] Create IdempotencyKeyOperationFilter for Swagger documentation
- [ ] Register middleware, store, and Swagger filter in Program.cs
