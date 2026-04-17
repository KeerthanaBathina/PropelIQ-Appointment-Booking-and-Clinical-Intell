# Task - task_003_be_cqrs_audit_log_access

## Requirement Reference

- User Story: us_096
- Story Location: .propel/context/tasks/EP-019/us_096/us_096.md
- Acceptance Criteria:
  - AC-3: Given audit log operations exist, When read and write operations are compared, Then writes use the command path (through Service layer) and reads use a separate optimized query path (CQRS).

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
| Backend | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |

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

Implement the CQRS (Command Query Responsibility Segregation) pattern for audit log access, separating the write model (append-only audit log ingestion through the Service layer) from the read model (optimized indexed query path for compliance reporting), satisfying AC-3, TR-013, and design Decision #5. The write side uses an `IAuditLogCommandService` that appends immutable audit entries through the standard Service → DataAccess pipeline with EF Core. The read side uses a dedicated `IAuditLogQueryService` backed by a read-optimized `AuditLogReadDbContext` configured with `AsNoTracking()` globally, leveraging PostgreSQL indexed views and raw SQL projections for fast compliance reporting queries without impacting transactional write performance. The two paths share the same underlying database but use separate DbContext instances to enforce separation of concerns.

## Dependent Tasks

- task_001_be_layered_architecture_enforcement — Requires layered architecture with UPACIP.Contracts for interface definitions.
- US_001 — Requires project scaffold with AuditLog entity.

## Impacted Components

- **NEW** `src/UPACIP.Contracts/Services/IAuditLogCommandService.cs` — Write-side interface: append-only audit log entry creation
- **NEW** `src/UPACIP.Contracts/Services/IAuditLogQueryService.cs` — Read-side interface: optimized query operations for audit log access
- **NEW** `src/UPACIP.Contracts/Models/AuditLogEntry.cs` — Write DTO: command model for creating audit entries
- **NEW** `src/UPACIP.Contracts/Models/AuditLogReadModel.cs` — Read DTO: flattened projection optimized for query results
- **NEW** `src/UPACIP.Contracts/Models/AuditLogQueryFilter.cs` — Filter DTO: date range, userId, entityType, action, pagination
- **NEW** `src/UPACIP.Service/AuditLog/AuditLogCommandService.cs` — Write implementation: validates and persists audit entries via EF Core
- **NEW** `src/UPACIP.Service/AuditLog/AuditLogQueryService.cs` — Read implementation: optimized queries using read-only DbContext
- **NEW** `src/UPACIP.DataAccess/AuditLogReadDbContext.cs` — Read-optimized DbContext with AsNoTracking, query-only configuration
- **NEW** `src/UPACIP.Api/Controllers/AuditLogController.cs` — API: separated command (POST) and query (GET) endpoints
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Ensure AuditLog entity is append-only (no Update/Delete override)
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register command and query services, AuditLogReadDbContext

## Implementation Plan

1. **Define `AuditLogEntry` write command DTO**: Create in `src/UPACIP.Contracts/Models/AuditLogEntry.cs`:
   ```csharp
   public class AuditLogEntry
   {
       public Guid UserId { get; init; }
       public string Action { get; init; }           // e.g., "Create", "Update", "Delete", "Access"
       public string EntityType { get; init; }        // e.g., "Patient", "Appointment"
       public Guid EntityId { get; init; }
       public string? OldValues { get; init; }        // JSON of previous state (for Update/Delete)
       public string? NewValues { get; init; }        // JSON of new state (for Create/Update)
       public string? IpAddress { get; init; }
       public string? CorrelationId { get; init; }
   }
   ```
   This is the command model — consumed by the write path. The `OldValues`/`NewValues` fields store JSON snapshots for compliance auditing per HIPAA requirements.

2. **Define `AuditLogReadModel` read projection DTO**: Create in `src/UPACIP.Contracts/Models/AuditLogReadModel.cs`:
   ```csharp
   public class AuditLogReadModel
   {
       public Guid Id { get; init; }
       public DateTime TimestampUtc { get; init; }
       public Guid UserId { get; init; }
       public string UserName { get; init; }          // Denormalized for read performance
       public string Action { get; init; }
       public string EntityType { get; init; }
       public Guid EntityId { get; init; }
       public string? OldValues { get; init; }
       public string? NewValues { get; init; }
       public string? IpAddress { get; init; }
       public string? CorrelationId { get; init; }
   }
   ```
   The read model includes denormalized fields (e.g., `UserName`) to avoid joins during read queries — a CQRS optimization that decouples read performance from write normalization.

3. **Define `AuditLogQueryFilter` filter DTO**: Create in `src/UPACIP.Contracts/Models/AuditLogQueryFilter.cs`:
   ```csharp
   public class AuditLogQueryFilter
   {
       public DateTime? FromUtc { get; init; }
       public DateTime? ToUtc { get; init; }
       public Guid? UserId { get; init; }
       public string? EntityType { get; init; }
       public string? Action { get; init; }
       public Guid? EntityId { get; init; }
       public string? CorrelationId { get; init; }
       public int Page { get; init; } = 1;
       public int PageSize { get; init; } = 50;
   }
   ```
   Supports multi-field filtering for compliance reporting. Default page size of 50 balances response payload with usability.

4. **Define `IAuditLogCommandService` interface (write path)**: Create in `src/UPACIP.Contracts/Services/IAuditLogCommandService.cs`:
   ```csharp
   public interface IAuditLogCommandService : IServiceBase
   {
       Task<Guid> AppendAsync(AuditLogEntry entry, CancellationToken ct = default);
       Task AppendBatchAsync(IReadOnlyList<AuditLogEntry> entries, CancellationToken ct = default);
   }
   ```
   - `AppendAsync` — single entry append.
   - `AppendBatchAsync` — batch append for bulk operations (e.g., import processes).
   - No `Update` or `Delete` methods — audit logs are immutable per HIPAA (DR-016).

5. **Define `IAuditLogQueryService` interface (read path)**: Create in `src/UPACIP.Contracts/Services/IAuditLogQueryService.cs`:
   ```csharp
   public interface IAuditLogQueryService : IServiceBase
   {
       Task<PagedResult<AuditLogReadModel>> QueryAsync(AuditLogQueryFilter filter, CancellationToken ct = default);
       Task<AuditLogReadModel?> GetByIdAsync(Guid auditLogId, CancellationToken ct = default);
       Task<List<AuditLogReadModel>> GetByEntityAsync(string entityType, Guid entityId, CancellationToken ct = default);
       Task<int> CountByDateRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
   }
   ```
   - `QueryAsync` — paginated, filtered query for compliance dashboards.
   - `GetByEntityAsync` — entity-specific audit trail (e.g., all changes to Patient with ID X).
   - `CountByDateRangeAsync` — summary count for compliance reporting metrics.

6. **Create `AuditLogReadDbContext` (read-optimized)**: Create in `src/UPACIP.DataAccess/AuditLogReadDbContext.cs`:
   ```csharp
   public class AuditLogReadDbContext : DbContext
   {
       public AuditLogReadDbContext(DbContextOptions<AuditLogReadDbContext> options) : base(options) { }

       public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

       protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
       {
           optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
       }

       protected override void OnModelCreating(ModelBuilder modelBuilder)
       {
           modelBuilder.Entity<AuditLogEntity>(entity =>
           {
               entity.ToTable("AuditLogs");
               entity.HasKey(e => e.Id);
               // Read-only: no navigation properties, no cascades
               // Index hints for common query patterns
               entity.HasIndex(e => e.TimestampUtc).HasDatabaseName("IX_AuditLogs_Timestamp");
               entity.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("IX_AuditLogs_Entity");
               entity.HasIndex(e => e.UserId).HasDatabaseName("IX_AuditLogs_UserId");
               entity.HasIndex(e => e.CorrelationId).HasDatabaseName("IX_AuditLogs_CorrelationId");
           });
       }
   }
   ```
   Key read-side optimizations:
   - `QueryTrackingBehavior.NoTracking` globally — no change tracker overhead for read queries.
   - No navigation properties — avoids lazy loading, forces explicit projections.
   - Dedicated indexes for common query patterns (timestamp range, entity lookup, user lookup, correlation).
   - Same underlying `AuditLogs` table — data consistency via single source of truth.

7. **Implement `AuditLogCommandService` (write path — AC-3)**: Create in `src/UPACIP.Service/AuditLog/AuditLogCommandService.cs`. Constructor injection of `ApplicationDbContext` (the standard write DbContext), `ILogger<AuditLogCommandService>`.

   **`AppendAsync`**:
   - Validate required fields: `Action`, `EntityType`, `EntityId` are non-empty.
   - Create `AuditLogEntity` from `AuditLogEntry`:
     ```csharp
     var entity = new AuditLogEntity
     {
         Id = Guid.NewGuid(),
         TimestampUtc = DateTime.UtcNow,
         UserId = entry.UserId,
         Action = entry.Action,
         EntityType = entry.EntityType,
         EntityId = entry.EntityId,
         OldValues = entry.OldValues,
         NewValues = entry.NewValues,
         IpAddress = entry.IpAddress,
         CorrelationId = entry.CorrelationId
     };
     ```
   - `_dbContext.AuditLogs.Add(entity)`.
   - `await _dbContext.SaveChangesAsync(ct)`.
   - Log: `AUDIT_LOG_APPENDED: Action={Action}, Entity={EntityType}/{EntityId}`.
   - Return `entity.Id`.

   **`AppendBatchAsync`**:
   - Validate all entries.
   - Map to entities, call `_dbContext.AuditLogs.AddRange(entities)`.
   - Single `SaveChangesAsync` for the batch.
   - Log: `AUDIT_LOG_BATCH_APPENDED: Count={Count}`.

   **Immutability enforcement**: Override `SaveChangesAsync` in `ApplicationDbContext` to reject `Modified` or `Deleted` states for `AuditLogEntity`:
   ```csharp
   var auditLogModifications = ChangeTracker.Entries<AuditLogEntity>()
       .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);
   if (auditLogModifications.Any())
       throw new InvalidOperationException("Audit log entries are immutable. Updates and deletes are prohibited.");
   ```

8. **Implement `AuditLogQueryService` (read path — AC-3)**: Create in `src/UPACIP.Service/AuditLog/AuditLogQueryService.cs`. Constructor injection of `AuditLogReadDbContext` (the read-optimized DbContext), `ILogger<AuditLogQueryService>`.

   **`QueryAsync`**: Build a composable `IQueryable<AuditLogEntity>` applying filters conditionally:
   ```csharp
   var query = _readContext.AuditLogs.AsQueryable();

   if (filter.FromUtc.HasValue)
       query = query.Where(a => a.TimestampUtc >= filter.FromUtc.Value);
   if (filter.ToUtc.HasValue)
       query = query.Where(a => a.TimestampUtc <= filter.ToUtc.Value);
   if (filter.UserId.HasValue)
       query = query.Where(a => a.UserId == filter.UserId.Value);
   if (!string.IsNullOrEmpty(filter.EntityType))
       query = query.Where(a => a.EntityType == filter.EntityType);
   if (!string.IsNullOrEmpty(filter.Action))
       query = query.Where(a => a.Action == filter.Action);
   if (filter.EntityId.HasValue)
       query = query.Where(a => a.EntityId == filter.EntityId.Value);
   if (!string.IsNullOrEmpty(filter.CorrelationId))
       query = query.Where(a => a.CorrelationId == filter.CorrelationId);
   ```
   - Count total matches: `await query.CountAsync(ct)`.
   - Apply pagination: `.OrderByDescending(a => a.TimestampUtc).Skip((page - 1) * pageSize).Take(pageSize)`.
   - Project to `AuditLogReadModel` via `.Select(a => new AuditLogReadModel { ... })`.
   - Return `PagedResult<AuditLogReadModel>`.

   **`GetByIdAsync`**: Single entry lookup by primary key.

   **`GetByEntityAsync`**: Filter by `EntityType` + `EntityId`, ordered by timestamp descending. Uses the composite `IX_AuditLogs_Entity` index.

   **`CountByDateRangeAsync`**: `_readContext.AuditLogs.CountAsync(a => a.TimestampUtc >= from && a.TimestampUtc <= to)`. Uses the `IX_AuditLogs_Timestamp` index.

9. **Implement `AuditLogController` with separated endpoints (AC-3)**: Create in `src/UPACIP.Api/Controllers/AuditLogController.cs`. Inject `IAuditLogCommandService` (write) and `IAuditLogQueryService` (read) separately — demonstrating CQRS at the controller level.

   **Write endpoint (command path through Service layer)**:
   ```csharp
   [HttpPost(Name = "CreateAuditLog")]
   [Authorize(Roles = "Admin,System")]
   public async Task<IActionResult> Create([FromBody] AuditLogEntry entry, CancellationToken ct)
   {
       var id = await _commandService.AppendAsync(entry, ct);
       return CreatedAtRoute("GetAuditLogById", new { id }, new { id });
   }
   ```

   **Read endpoints (optimized query path)**:
   ```csharp
   [HttpGet(Name = "QueryAuditLogs")]
   [Authorize(Roles = "Admin")]
   public async Task<IActionResult> Query([FromQuery] AuditLogQueryFilter filter, CancellationToken ct)
   {
       var result = await _queryService.QueryAsync(filter, ct);
       return Ok(_hateoasWrapper.WrapCollection(result, "QueryAuditLogs"));
   }

   [HttpGet("{id}", Name = "GetAuditLogById")]
   [Authorize(Roles = "Admin")]
   public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
   {
       var entry = await _queryService.GetByIdAsync(id, ct);
       if (entry is null) return NotFound();
       return Ok(_hateoasWrapper.WrapResource(entry, "GetAuditLogById", new { id }));
   }

   [HttpGet("entity/{entityType}/{entityId}", Name = "GetAuditLogsByEntity")]
   [Authorize(Roles = "Admin")]
   public async Task<IActionResult> GetByEntity(string entityType, Guid entityId, CancellationToken ct)
   {
       var entries = await _queryService.GetByEntityAsync(entityType, entityId, ct);
       return Ok(entries);
   }

   [HttpGet("count", Name = "GetAuditLogCount")]
   [Authorize(Roles = "Admin")]
   public async Task<IActionResult> Count([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
   {
       var count = await _queryService.CountByDateRangeAsync(fromUtc, toUtc, ct);
       return Ok(new { count, fromUtc, toUtc });
   }
   ```

10. **Register services and read DbContext in DI**: In `Program.cs`:
    ```csharp
    // CQRS — Audit Log
    builder.Services.AddDbContext<AuditLogReadDbContext>(options =>
        options.UseNpgsql(connectionString));
    builder.Services.AddScoped<IAuditLogCommandService, AuditLogCommandService>();
    builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();
    ```
    The `AuditLogReadDbContext` uses the same connection string as `ApplicationDbContext` but is a separate DbContext type with read-only configuration. EF Core DI resolves the correct context per service based on constructor injection.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   └── Controllers/
│   ├── UPACIP.Contracts/                            ← from task_001
│   │   ├── UPACIP.Contracts.csproj
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs                       ← from task_001
│   │   │   ├── PagedResult.cs                       ← from task_001
│   │   │   ├── HateoasLink.cs                       ← from task_002
│   │   │   ├── HateoasResponse.cs                   ← from task_002
│   │   │   └── PagedHateoasResponse.cs              ← from task_002
│   │   └── Services/
│   │       └── IServiceBase.cs                      ← from task_001
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Hateoas/                                 ← from task_002
│   │   │   ├── IHateoasLinkGenerator.cs
│   │   │   ├── HateoasLinkGenerator.cs
│   │   │   └── HateoasResponseWrapper.cs
│   │   └── ...
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   └── AuditLogEntity.cs                    ← existing entity
│       └── Configurations/
├── tests/
│   └── UPACIP.ArchTests/                            ← from task_001
├── Server/
├── app/
├── config/
└── scripts/
```

> Assumes task_001 (layered architecture), task_002 (HATEOAS), and US_001 (project scaffold with AuditLog entity) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Contracts/Services/IAuditLogCommandService.cs | Write interface: AppendAsync, AppendBatchAsync (no update/delete) |
| CREATE | src/UPACIP.Contracts/Services/IAuditLogQueryService.cs | Read interface: QueryAsync, GetByIdAsync, GetByEntityAsync, CountByDateRangeAsync |
| CREATE | src/UPACIP.Contracts/Models/AuditLogEntry.cs | Write DTO: command model for audit entry creation |
| CREATE | src/UPACIP.Contracts/Models/AuditLogReadModel.cs | Read DTO: denormalized projection for query results |
| CREATE | src/UPACIP.Contracts/Models/AuditLogQueryFilter.cs | Filter DTO: date range, userId, entityType, action, pagination |
| CREATE | src/UPACIP.Service/AuditLog/AuditLogCommandService.cs | Write implementation: append-only via ApplicationDbContext |
| CREATE | src/UPACIP.Service/AuditLog/AuditLogQueryService.cs | Read implementation: optimized queries via AuditLogReadDbContext |
| CREATE | src/UPACIP.DataAccess/AuditLogReadDbContext.cs | Read-only DbContext: NoTracking, indexed views, no navigation properties |
| CREATE | src/UPACIP.Api/Controllers/AuditLogController.cs | Separated POST (command) and GET (query) endpoints |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add immutability guard for AuditLogEntity (reject Update/Delete) |
| MODIFY | src/UPACIP.Api/Program.cs | Register AuditLogReadDbContext, command and query services |

## External References

- [CQRS Pattern — Microsoft Architecture Guide](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)
- [AsNoTracking — EF Core Performance](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries)
- [Audit Logging — HIPAA Security Rule §164.312(b)](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)
- [PostgreSQL Indexes — Performance Tuning](https://www.postgresql.org/docs/16/indexes.html)

## Build Commands

```powershell
# Build Contracts project
dotnet build src/UPACIP.Contracts/UPACIP.Contracts.csproj

# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Audit log write uses `IAuditLogCommandService` → `ApplicationDbContext` (command path through Service layer) (AC-3)
- [ ] Audit log read uses `IAuditLogQueryService` → `AuditLogReadDbContext` (separate optimized query path) (AC-3)
- [ ] Write and read services use different DbContext instances (CQRS separation)
- [ ] AuditLogReadDbContext has `NoTracking` globally configured
- [ ] AuditLogEntity cannot be modified or deleted (immutability guard throws InvalidOperationException)
- [ ] Query endpoint supports multi-field filtering (date range, userId, entityType, action)
- [ ] Paginated query returns PagedResult with correct totalCount and page metadata
- [ ] Controller injects separate command and query service interfaces
- [ ] Indexes exist for TimestampUtc, EntityType+EntityId, UserId, CorrelationId

## Implementation Checklist

- [ ] Define AuditLogEntry write command DTO in UPACIP.Contracts
- [ ] Define AuditLogReadModel read projection DTO in UPACIP.Contracts
- [ ] Define AuditLogQueryFilter with date range, entity, user, pagination fields
- [ ] Define IAuditLogCommandService interface (append-only, no update/delete)
- [ ] Define IAuditLogQueryService interface (query, getById, getByEntity, count)
- [ ] Create AuditLogReadDbContext with NoTracking and dedicated indexes
- [ ] Implement AuditLogCommandService with immutability enforcement
- [ ] Implement AuditLogQueryService with composable filtered queries
