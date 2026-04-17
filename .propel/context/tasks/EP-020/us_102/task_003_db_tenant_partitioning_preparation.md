# Task - task_003_db_tenant_partitioning_preparation

## Requirement Reference

- User Story: us_102
- Story Location: .propel/context/tasks/EP-020/us_102/us_102.md
- Acceptance Criteria:
  - AC-4: Given database partitioning preparation is needed, When the data layer is designed, Then tables support future tenant-based partitioning without schema changes (tenant_id column where applicable).

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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| ORM Provider | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |

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

Add a `TenantId` column to all tenant-scoped database entities as preparation for future multi-tenant partitioning (NFR-027), using an EF Core migration that is backward-compatible with the current single-tenant deployment. The `TenantId` column is a non-nullable `Guid` with a default value representing the single "default" tenant in Phase 1. A global query filter in the `DbContext` ensures all queries automatically include the `TenantId` predicate, making the transition to multi-tenant partitioning transparent to the application layer. Composite indexes are added on `(TenantId, Id)` for partition-key-aligned lookups. The schema is designed so that PostgreSQL list or hash partitioning can be enabled in Phase 2 by issuing `ALTER TABLE ... PARTITION BY` without modifying the application code or EF Core model (AC-4).

## Dependent Tasks

- US_001 — Requires backend API scaffold with EF Core configured.
- US_003 — Requires database schema with core entity tables.

## Impacted Components

- **CREATE** `src/UPACIP.Contracts/MultiTenancy/ITenantEntity.cs` — Interface: TenantId property
- **CREATE** `src/UPACIP.Service/MultiTenancy/TenantContext.cs` — Holds current tenant ID (default for Phase 1)
- **CREATE** `src/UPACIP.Service/MultiTenancy/ITenantProvider.cs` — Interface for tenant resolution
- **CREATE** `src/UPACIP.Service/MultiTenancy/DefaultTenantProvider.cs` — Phase 1: returns default tenant ID
- **MODIFY** `src/UPACIP.DataAccess/Entities/` — Add TenantId to all tenant-scoped entities
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add global query filter for TenantId
- **CREATE** `src/UPACIP.DataAccess/Migrations/XXXXXXXX_AddTenantIdColumn.cs` — EF Core migration

## Implementation Plan

1. **Define `ITenantEntity` interface**: Create `src/UPACIP.Contracts/MultiTenancy/ITenantEntity.cs`:
   ```csharp
   public interface ITenantEntity
   {
       Guid TenantId { get; set; }
   }
   ```
   - All tenant-scoped entities implement this interface.
   - Enables the global query filter to apply generically across all entities.
   - System-level entities (e.g., `AuditLog`, `FeatureFlag`) do NOT implement this interface — they are tenant-agnostic.

2. **Define default tenant constant and `ITenantProvider`**: Create `src/UPACIP.Service/MultiTenancy/ITenantProvider.cs` and `DefaultTenantProvider.cs`:
   ```csharp
   // ITenantProvider.cs
   public interface ITenantProvider
   {
       Guid GetCurrentTenantId();
   }

   // DefaultTenantProvider.cs
   public sealed class DefaultTenantProvider : ITenantProvider
   {
       /// <summary>
       /// Phase 1 default tenant. All data belongs to this single tenant.
       /// In Phase 2, this is replaced by a header/claim-based resolver.
       /// </summary>
       public static readonly Guid DefaultTenantId =
           Guid.Parse("00000000-0000-0000-0000-000000000001");

       public Guid GetCurrentTenantId() => DefaultTenantId;
   }
   ```
   Key decisions:
   - **Deterministic GUID** (`00000000-0000-0000-0000-000000000001`) — easily identifiable as the default tenant in queries and logs.
   - **`ITenantProvider`** abstraction — in Phase 2, swap `DefaultTenantProvider` with a `HttpContextTenantProvider` that reads tenant ID from a JWT claim or request header. Zero changes to entities or query filters.
   - Registered as `Scoped` — each HTTP request resolves the tenant once.

3. **Create `TenantContext`**: Create `src/UPACIP.Service/MultiTenancy/TenantContext.cs`:
   ```csharp
   public sealed class TenantContext
   {
       public Guid TenantId { get; }

       public TenantContext(ITenantProvider provider)
       {
           TenantId = provider.GetCurrentTenantId();
       }
   }
   ```
   Injected into `DbContext` and services. Encapsulates the resolved tenant ID for the current request scope.

4. **Add `TenantId` to tenant-scoped entities (AC-4)**: Update all entities in `src/UPACIP.DataAccess/Entities/` that hold tenant-specific data:
   ```csharp
   // Example: PatientEntity
   public class PatientEntity : ITenantEntity
   {
       public Guid Id { get; set; }
       public Guid TenantId { get; set; }
       // ... existing properties
   }

   // Example: AppointmentEntity
   public class AppointmentEntity : ITenantEntity
   {
       public Guid Id { get; set; }
       public Guid TenantId { get; set; }
       // ... existing properties
   }

   // Example: ProviderEntity
   public class ProviderEntity : ITenantEntity
   {
       public Guid Id { get; set; }
       public Guid TenantId { get; set; }
       // ... existing properties
   }
   ```
   Entities that should receive `TenantId`:
   - `PatientEntity` — patient demographics are tenant-scoped.
   - `AppointmentEntity` — appointments belong to a tenant's facility.
   - `ProviderEntity` — providers are affiliated with a tenant.
   - `ClinicalDocumentEntity` — documents belong to a tenant's patients.
   - `IntakeFormEntity` — intake data is tenant-specific.
   - `NotificationEntity` — notifications are scoped to tenant users.

   Entities that should NOT receive `TenantId`:
   - `AuditLogEntity` — system-wide audit trail (cross-tenant).
   - `UserEntity` — users may belong to multiple tenants in Phase 2 (handled via a join table).
   - `FeatureFlagEntity` (if persisted) — feature flags are system-wide.

5. **Configure `ApplicationDbContext` with global query filter (AC-4)**: Update `src/UPACIP.DataAccess/ApplicationDbContext.cs`:
   ```csharp
   public class ApplicationDbContext : DbContext
   {
       private readonly TenantContext _tenantContext;

       public ApplicationDbContext(
           DbContextOptions<ApplicationDbContext> options,
           TenantContext tenantContext)
           : base(options)
       {
           _tenantContext = tenantContext;
       }

       protected override void OnModelCreating(ModelBuilder modelBuilder)
       {
           base.OnModelCreating(modelBuilder);

           // Apply global query filter to all ITenantEntity implementations
           foreach (var entityType in modelBuilder.Model.GetEntityTypes())
           {
               if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
               {
                   var method = typeof(ApplicationDbContext)
                       .GetMethod(nameof(ApplyTenantFilter),
                           System.Reflection.BindingFlags.NonPublic |
                           System.Reflection.BindingFlags.Static)!
                       .MakeGenericMethod(entityType.ClrType);

                   method.Invoke(null, new object[] { modelBuilder, _tenantContext });
               }
           }

           // Configure TenantId column and indexes
           foreach (var entityType in modelBuilder.Model.GetEntityTypes())
           {
               if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
               {
                   modelBuilder.Entity(entityType.ClrType)
                       .Property(nameof(ITenantEntity.TenantId))
                       .IsRequired()
                       .HasDefaultValue(DefaultTenantProvider.DefaultTenantId);

                   // Composite index for partition-aligned queries
                   modelBuilder.Entity(entityType.ClrType)
                       .HasIndex(nameof(ITenantEntity.TenantId), "Id")
                       .HasDatabaseName(
                           $"IX_{entityType.ClrType.Name}_{nameof(ITenantEntity.TenantId)}_Id");
               }
           }
       }

       private static void ApplyTenantFilter<T>(
           ModelBuilder modelBuilder,
           TenantContext tenantContext) where T : class, ITenantEntity
       {
           modelBuilder.Entity<T>()
               .HasQueryFilter(e => e.TenantId == tenantContext.TenantId);
       }

       public override int SaveChanges()
       {
           SetTenantId();
           return base.SaveChanges();
       }

       public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
       {
           SetTenantId();
           return base.SaveChangesAsync(cancellationToken);
       }

       private void SetTenantId()
       {
           foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
               .Where(e => e.State == EntityState.Added))
           {
               if (entry.Entity.TenantId == Guid.Empty)
               {
                   entry.Entity.TenantId = _tenantContext.TenantId;
               }
           }
       }
   }
   ```
   Key behaviors (AC-4):
   - **Global query filter** — every LINQ query against a tenant-scoped entity automatically includes `WHERE TenantId = @currentTenantId`. No manual filtering required.
   - **`SaveChanges` interceptor** — automatically sets `TenantId` on new entities if not already set. Prevents accidental creation of entities without tenant context.
   - **Default value** — `HasDefaultValue(DefaultTenantProvider.DefaultTenantId)` ensures existing rows and new rows without explicit `TenantId` receive the default tenant ID. This makes the migration backward-compatible with existing data.
   - **Composite index `(TenantId, Id)`** — when PostgreSQL partitioning is enabled in Phase 2 (by `TenantId`), this index aligns with the partition key for efficient lookups.
   - **Reflection-based filter application** — iterates all `ITenantEntity` implementations and applies the filter generically. Adding `ITenantEntity` to a new entity automatically includes it in the filter — no manual configuration per entity.

6. **Create EF Core migration**: Generate the migration:
   ```powershell
   dotnet ef migrations add AddTenantIdColumn \
       --project src/UPACIP.DataAccess \
       --startup-project src/UPACIP.Api
   ```
   The migration will:
   - Add `TenantId` column (non-nullable, `uuid` type) to all tenant-scoped tables.
   - Set default value to `00000000-0000-0000-0000-000000000001` for existing rows.
   - Create composite indexes `IX_{Table}_TenantId_Id`.
   - The migration is backward-compatible — existing queries continue to work because the global filter returns all rows matching the default tenant ID.

7. **Register multi-tenancy services**: Update `Program.cs`:
   ```csharp
   builder.Services.AddScoped<ITenantProvider, DefaultTenantProvider>();
   builder.Services.AddScoped<TenantContext>();
   ```
   - `Scoped` lifetime — one `TenantContext` per HTTP request.
   - In Phase 2, replace `DefaultTenantProvider` registration with the multi-tenant implementation.

8. **Document Phase 2 partitioning path**: The schema is designed for seamless PostgreSQL partitioning:
   ```sql
   -- Phase 2: Convert to partitioned table (no application changes needed)
   -- 1. Create partitioned version of table
   CREATE TABLE patients_partitioned (LIKE patients INCLUDING ALL)
       PARTITION BY LIST (tenant_id);

   -- 2. Create partitions per tenant
   CREATE TABLE patients_tenant_001 PARTITION OF patients_partitioned
       FOR VALUES IN ('00000000-0000-0000-0000-000000000001');

   CREATE TABLE patients_tenant_002 PARTITION OF patients_partitioned
       FOR VALUES IN ('00000000-0000-0000-0000-000000000002');

   -- 3. Migrate data (online, using pg_partman or manual INSERT...SELECT)
   -- 4. Swap table names (requires brief write lock)
   ```
   Requirements for Phase 2 partitioning:
   - `TenantId` column already exists on all tables (this task).
   - Composite indexes include `TenantId` as the leading column (this task).
   - Global query filter already filters by `TenantId` (this task).
   - Only the `ITenantProvider` implementation changes — resolves tenant from JWT/header instead of returning default.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   └── Middleware/
│   ├── UPACIP.Contracts/
│   │   ├── UPACIP.Contracts.csproj
│   │   └── MultiTenancy/                       ← new directory
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── MultiTenancy/                       ← new directory
│   │   ├── Idempotency/
│   │   └── Configuration/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Migrations/
├── tests/
├── e2e/
├── scripts/
├── app/
└── config/
```

> Assumes US_001 (backend API scaffold), US_003 (database schema with core entities), and task_001/task_002 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Contracts/MultiTenancy/ITenantEntity.cs | Interface: TenantId property for tenant-scoped entities |
| CREATE | src/UPACIP.Service/MultiTenancy/ITenantProvider.cs | Interface: GetCurrentTenantId() |
| CREATE | src/UPACIP.Service/MultiTenancy/DefaultTenantProvider.cs | Phase 1: returns deterministic default tenant GUID |
| CREATE | src/UPACIP.Service/MultiTenancy/TenantContext.cs | Scoped: holds resolved tenant ID for current request |
| MODIFY | src/UPACIP.DataAccess/Entities/ | Add TenantId (Guid) to Patient, Appointment, Provider, ClinicalDocument, IntakeForm, Notification |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Global query filter, SaveChanges TenantId setter, composite indexes |
| CREATE | src/UPACIP.DataAccess/Migrations/XXXXXXXX_AddTenantIdColumn.cs | Migration: add TenantId column, default value, composite indexes |

## External References

- [EF Core — Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core — Multi-tenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy)
- [PostgreSQL — Table Partitioning](https://www.postgresql.org/docs/16/ddl-partitioning.html)
- [PostgreSQL — List Partitioning](https://www.postgresql.org/docs/16/ddl-partitioning.html#DDL-PARTITIONING-DECLARATIVE)
- [EF Core — SaveChanges Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#savechanges-interception)
- [NFR-027 — Multi-tenant Partition Preparation](design.md line 49)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Generate migration
dotnet ef migrations add AddTenantIdColumn --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migration
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] EF Core migration adds TenantId column to all tenant-scoped tables (AC-4)
- [ ] Existing rows receive default tenant ID via column default value (AC-4)
- [ ] Global query filter includes TenantId predicate on all ITenantEntity queries
- [ ] SaveChanges automatically sets TenantId on new entities
- [ ] Composite index (TenantId, Id) created on all tenant-scoped tables
- [ ] System-level entities (AuditLog) do NOT have TenantId
- [ ] Schema supports PostgreSQL LIST partitioning without further schema changes (AC-4)

## Implementation Checklist

- [ ] Create ITenantEntity interface with TenantId property in UPACIP.Contracts
- [ ] Create ITenantProvider interface and DefaultTenantProvider with deterministic GUID
- [ ] Create TenantContext with constructor-injected ITenantProvider
- [ ] Add TenantId to all tenant-scoped entities (Patient, Appointment, Provider, etc.)
- [ ] Configure ApplicationDbContext with global query filter for ITenantEntity
- [ ] Add SaveChanges interceptor to auto-set TenantId on new entities
- [ ] Generate and apply EF Core migration with default value and composite indexes
- [ ] Register ITenantProvider and TenantContext as Scoped in Program.cs
