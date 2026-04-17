# Task - task_002_be_hateoas_restful_response_wrappers

## Requirement Reference

- User Story: us_096
- Story Location: .propel/context/tasks/EP-019/us_096/us_096.md
- Acceptance Criteria:
  - AC-2: Given API endpoints are implemented, When responses are returned, Then they include HATEOAS links for discoverable navigation (self, next, previous, related resources).
- Edge Case:
  - How does the system handle HATEOAS for paginated collections? Paginated responses include first, last, next, and previous links with page size and total count metadata.

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
| Backend | Swagger (Swashbuckle) | 6.x |

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

Implement HATEOAS (Hypermedia as the Engine of Application State) link generation for RESTful API responses, satisfying AC-2 and TR-011. This task creates a reusable `IHateoasLinkGenerator` service that produces hypermedia links (self, next, previous, first, last, related) for single-resource and paginated collection responses. A `HateoasResponseWrapper` enriches `ApiResponse<T>` with a `Links` dictionary. Paginated endpoints return `PagedHateoasResponse<T>` with full pagination links including page size and total count metadata (edge case 2). An `IUrlHelper`-based link builder generates absolute URLs from route names, ensuring links remain valid under reverse proxies.

## Dependent Tasks

- task_001_be_layered_architecture_enforcement — Requires `ApiResponse<T>`, `PagedResult<T>`, and `UPACIP.Contracts` project.
- US_001 — Requires project scaffold with controller routing.

## Impacted Components

- **NEW** `src/UPACIP.Contracts/Models/HateoasLink.cs` — DTO representing a single hypermedia link (href, rel, method)
- **NEW** `src/UPACIP.Contracts/Models/HateoasResponse.cs` — Response wrapper adding Links collection to ApiResponse
- **NEW** `src/UPACIP.Contracts/Models/PagedHateoasResponse.cs` — Paginated response with first/last/next/previous links + pagination metadata
- **NEW** `src/UPACIP.Service/Hateoas/IHateoasLinkGenerator.cs` — Interface for generating HATEOAS links from route names
- **NEW** `src/UPACIP.Service/Hateoas/HateoasLinkGenerator.cs` — Implementation using IUrlHelper and IHttpContextAccessor
- **NEW** `src/UPACIP.Service/Hateoas/HateoasResponseWrapper.cs` — Wraps ApiResponse/PagedResult with generated links
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register HateoasLinkGenerator and HateoasResponseWrapper in DI

## Implementation Plan

1. **Create `HateoasLink` DTO**: Create in `src/UPACIP.Contracts/Models/HateoasLink.cs`:
   ```csharp
   public class HateoasLink
   {
       public string Href { get; init; }
       public string Rel { get; init; }
       public string Method { get; init; }
   }
   ```
   - `Href` — Absolute URL to the resource (e.g., `https://host/api/patients/123`).
   - `Rel` — Link relation type: `self`, `next`, `previous`, `first`, `last`, `collection`, or a custom relation like `appointments`.
   - `Method` — HTTP method: `GET`, `POST`, `PUT`, `DELETE`.

2. **Create `HateoasResponse<T>` for single-resource responses (AC-2)**: Create in `src/UPACIP.Contracts/Models/HateoasResponse.cs`:
   ```csharp
   public class HateoasResponse<T>
   {
       public T Data { get; init; }
       public bool Success { get; init; }
       public string? Message { get; init; }
       public List<HateoasLink> Links { get; init; } = new();
   }
   ```
   Used for individual resource responses (e.g., GET `/api/patients/{id}`). Contains:
   - `self` link — the canonical URL for this resource.
   - Related resource links — e.g., `appointments` (GET `/api/patients/{id}/appointments`), `auditLogs` (GET `/api/audit-logs?entityId={id}`).
   - Action links — e.g., `update` (PUT), `delete` (DELETE) when the caller has permission.

3. **Create `PagedHateoasResponse<T>` for paginated collections (AC-2, edge case 2)**: Create in `src/UPACIP.Contracts/Models/PagedHateoasResponse.cs`:
   ```csharp
   public class PagedHateoasResponse<T>
   {
       public List<T> Items { get; init; }
       public int Page { get; init; }
       public int PageSize { get; init; }
       public int TotalCount { get; init; }
       public int TotalPages { get; init; }
       public List<HateoasLink> Links { get; init; } = new();
   }
   ```
   Pagination links:
   - `self` — current page URL (e.g., `/api/patients?page=3&pageSize=20`).
   - `first` — first page URL (e.g., `/api/patients?page=1&pageSize=20`).
   - `last` — last page URL (e.g., `/api/patients?page={totalPages}&pageSize=20`).
   - `next` — next page URL if `HasNext` is true (omitted on last page).
   - `previous` — previous page URL if `HasPrevious` is true (omitted on first page).

4. **Define `IHateoasLinkGenerator` interface**: Create in `src/UPACIP.Service/Hateoas/IHateoasLinkGenerator.cs`:
   ```csharp
   public interface IHateoasLinkGenerator
   {
       HateoasLink GenerateLink(string routeName, object routeValues, string rel, string method);
       List<HateoasLink> GenerateResourceLinks(string resourceRouteName, object routeValues, List<(string RouteName, object RouteValues, string Rel, string Method)> relatedLinks);
       List<HateoasLink> GeneratePaginationLinks(string routeName, int page, int pageSize, int totalPages);
   }
   ```
   - `GenerateLink` — creates a single `HateoasLink` from a named route.
   - `GenerateResourceLinks` — creates `self` + related links for a single resource.
   - `GeneratePaginationLinks` — creates `self`, `first`, `last`, `next` (conditional), `previous` (conditional) for paginated results.

5. **Implement `HateoasLinkGenerator` (AC-2)**: Create in `src/UPACIP.Service/Hateoas/HateoasLinkGenerator.cs`. Constructor injection of `IHttpContextAccessor` and `LinkGenerator` (.NET built-in).

   **`GenerateLink`**: Use `LinkGenerator.GetUriByRouteValues(httpContext, routeName, routeValues)` to produce absolute URLs. This handles reverse proxy scenarios (X-Forwarded-Host, X-Forwarded-Proto) when `ForwardedHeadersOptions` is configured. Return a `HateoasLink` with the generated `Href`, provided `Rel`, and `Method`.

   **`GenerateResourceLinks`**: Generate `self` link from the primary route, then iterate `relatedLinks` to build additional `HateoasLink` entries. Return the combined list.

   **`GeneratePaginationLinks`** (edge case 2): Build pagination links:
   ```csharp
   var links = new List<HateoasLink>();
   links.Add(GenerateLink(routeName, new { page, pageSize }, "self", "GET"));
   links.Add(GenerateLink(routeName, new { page = 1, pageSize }, "first", "GET"));
   links.Add(GenerateLink(routeName, new { page = totalPages, pageSize }, "last", "GET"));

   if (page < totalPages)
       links.Add(GenerateLink(routeName, new { page = page + 1, pageSize }, "next", "GET"));
   if (page > 1)
       links.Add(GenerateLink(routeName, new { page = page - 1, pageSize }, "previous", "GET"));

   return links;
   ```

6. **Implement `HateoasResponseWrapper`**: Create in `src/UPACIP.Service/Hateoas/HateoasResponseWrapper.cs`. Constructor injection of `IHateoasLinkGenerator`.

   **`WrapResource<T>`**: Takes a resource `T`, a route name, route values, and optional related links. Returns `HateoasResponse<T>` with populated `Links`.
   ```csharp
   public HateoasResponse<T> WrapResource<T>(
       T data,
       string routeName,
       object routeValues,
       List<(string RouteName, object RouteValues, string Rel, string Method)>? relatedLinks = null)
   {
       var links = _linkGenerator.GenerateResourceLinks(routeName, routeValues, relatedLinks ?? new());
       return new HateoasResponse<T> { Data = data, Success = true, Links = links };
   }
   ```

   **`WrapCollection<T>`**: Takes a `PagedResult<T>`, a route name, and current page/pageSize. Returns `PagedHateoasResponse<T>` with pagination links.
   ```csharp
   public PagedHateoasResponse<T> WrapCollection<T>(
       PagedResult<T> pagedResult,
       string routeName)
   {
       var links = _linkGenerator.GeneratePaginationLinks(
           routeName, pagedResult.Page, pagedResult.PageSize, pagedResult.TotalPages);
       return new PagedHateoasResponse<T>
       {
           Items = pagedResult.Items,
           Page = pagedResult.Page,
           PageSize = pagedResult.PageSize,
           TotalCount = pagedResult.TotalCount,
           TotalPages = pagedResult.TotalPages,
           Links = links
       };
   }
   ```

7. **Add named routes to existing controllers**: Existing controller actions need `Name` properties on their route attributes to enable link generation. For example:
   ```csharp
   [HttpGet("{id}", Name = "GetPatientById")]
   public async Task<IActionResult> GetById(Guid id) { ... }

   [HttpGet(Name = "GetPatients")]
   public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20) { ... }
   ```
   Each controller should:
   - Inject `HateoasResponseWrapper`.
   - Wrap single-resource responses with `WrapResource()` including `self` and related links.
   - Wrap paginated responses with `WrapCollection()` for full pagination links.
   - Include action links (`update`, `delete`) only when the caller's role permits the action (check `User.IsInRole()`).

8. **Register services in DI**: In `Program.cs`:
   ```csharp
   builder.Services.AddHttpContextAccessor();
   builder.Services.AddScoped<IHateoasLinkGenerator, HateoasLinkGenerator>();
   builder.Services.AddScoped<HateoasResponseWrapper>();
   ```

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   └── appsettings.json
│   ├── UPACIP.Contracts/                            ← from task_001
│   │   ├── UPACIP.Contracts.csproj
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs                       ← from task_001
│   │   │   └── PagedResult.cs                       ← from task_001
│   │   └── Services/
│   │       └── IServiceBase.cs                      ← from task_001
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   └── ...
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       └── ...
├── tests/
│   └── UPACIP.ArchTests/                            ← from task_001
│       ├── UPACIP.ArchTests.csproj
│       └── ArchitectureTests.cs
├── Server/
├── app/
├── config/
└── scripts/
```

> Assumes task_001 (layered architecture enforcement) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Contracts/Models/HateoasLink.cs | DTO: href, rel, method for a single hypermedia link |
| CREATE | src/UPACIP.Contracts/Models/HateoasResponse.cs | Single-resource response with Links collection |
| CREATE | src/UPACIP.Contracts/Models/PagedHateoasResponse.cs | Paginated response with first/last/next/previous links + metadata |
| CREATE | src/UPACIP.Service/Hateoas/IHateoasLinkGenerator.cs | Interface: link generation from named routes |
| CREATE | src/UPACIP.Service/Hateoas/HateoasLinkGenerator.cs | Implementation: absolute URL generation via LinkGenerator |
| CREATE | src/UPACIP.Service/Hateoas/HateoasResponseWrapper.cs | Wraps ApiResponse/PagedResult with HATEOAS links |
| MODIFY | src/UPACIP.Api/Program.cs | Register IHateoasLinkGenerator, HateoasResponseWrapper in DI |

## External References

- [HATEOAS — Richardson Maturity Model Level 3](https://martinfowler.com/articles/richardsonMaturityModel.html)
- [ASP.NET Core Link Generation — LinkGenerator](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-8.0#link-generation)
- [RESTful API Design — Microsoft Guidelines](https://learn.microsoft.com/en-us/azure/architecture/best-practices/api-design)
- [JSON:API Pagination — Specification](https://jsonapi.org/format/#fetching-pagination)

## Build Commands

```powershell
# Build Contracts project
dotnet build src/UPACIP.Contracts/UPACIP.Contracts.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Single-resource GET response includes `self` link with absolute URL (AC-2)
- [ ] Single-resource response includes related resource links (e.g., appointments, auditLogs) (AC-2)
- [ ] Paginated GET response includes `first`, `last` links (edge case 2)
- [ ] Paginated response includes `next` link when not on last page (edge case 2)
- [ ] Paginated response includes `previous` link when not on first page (edge case 2)
- [ ] Paginated response omits `next` on last page and `previous` on first page
- [ ] Pagination metadata includes page, pageSize, totalCount, totalPages (edge case 2)
- [ ] Generated URLs are absolute and respect X-Forwarded-Host/Proto headers
- [ ] Action links (update, delete) are conditionally included based on user role

## Implementation Checklist

- [ ] Create HateoasLink DTO with href, rel, method properties
- [ ] Create HateoasResponse<T> for single-resource responses with Links
- [ ] Create PagedHateoasResponse<T> with pagination links and metadata
- [ ] Define IHateoasLinkGenerator interface with resource and pagination methods
- [ ] Implement HateoasLinkGenerator using ASP.NET Core LinkGenerator
- [ ] Implement HateoasResponseWrapper with WrapResource and WrapCollection
- [ ] Add named routes to existing controllers for link generation
- [ ] Register HATEOAS services in DI container
