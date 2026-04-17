# Task - task_001_be_xunit_moq_unit_testing_infrastructure

## Requirement Reference

- User Story: us_097
- Story Location: .propel/context/tasks/EP-019/us_097/us_097.md
- Acceptance Criteria:
  - AC-1: Given the test project is scaffolded, When a developer writes a unit test, Then xUnit test runner discovers and executes tests with Moq available for dependency mocking.
  - AC-3: Given tests are executed, When the test suite completes, Then the system enforces 80% code coverage for critical business logic paths.
- Edge Case:
  - What happens when a test depends on external services (AI, SMS)? Mock/stub implementations are used for unit tests; E2E tests use test doubles or sandbox environments.

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
| Testing | xUnit | 2.x |
| Testing | Moq | 4.x |
| Testing | coverlet.collector | 6.x |
| Testing | Microsoft.NET.Test.Sdk | 17.x |
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

Scaffold the xUnit unit testing infrastructure with Moq dependency mocking, test fixture conventions, and mock/stub patterns for external service dependencies (AC-1, AC-3, TR-029, edge case 1). This task creates two unit test projects — `UPACIP.Service.Tests` for Service layer logic and `UPACIP.Api.Tests` for controller/middleware testing — each pre-configured with xUnit 2.x, Moq 4.x, coverlet.collector for coverage instrumentation, and FluentAssertions for readable assertions. A shared `UPACIP.Tests.Common` project provides reusable test fixtures, mock factories for external services (AI gateway, SMS provider, Redis cache), and an in-memory `ApplicationDbContext` factory for database-dependent tests. Sample tests demonstrate the conventions: Arrange/Act/Assert structure, `[Fact]`/`[Theory]` attribute usage, Moq setup/verify patterns, and mock substitution for external dependencies.

## Dependent Tasks

- US_001 — Requires backend project scaffold with Service, Api, and DataAccess projects.
- US_096 task_001 — Requires UPACIP.Contracts for service interface definitions.

## Impacted Components

- **NEW** `tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj` — Unit test project for Service layer
- **NEW** `tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj` — Unit test project for Api layer (controllers, middleware)
- **NEW** `tests/UPACIP.Tests.Common/UPACIP.Tests.Common.csproj` — Shared test utilities, fixtures, mock factories
- **NEW** `tests/UPACIP.Tests.Common/Fixtures/DbContextFixture.cs` — In-memory ApplicationDbContext factory
- **NEW** `tests/UPACIP.Tests.Common/Mocks/MockAiGatewayFactory.cs` — Mock factory for AI gateway service
- **NEW** `tests/UPACIP.Tests.Common/Mocks/MockExternalServiceFactory.cs` — Mock factory for SMS, email, external HTTP
- **NEW** `tests/UPACIP.Service.Tests/AuditLog/AuditLogCommandServiceTests.cs` — Sample unit tests demonstrating conventions
- **NEW** `tests/UPACIP.Api.Tests/Controllers/AuditLogControllerTests.cs` — Sample controller tests with Moq
- **MODIFY** `UPACIP.sln` — Add test projects to solution

## Implementation Plan

1. **Create `UPACIP.Tests.Common` shared test utilities project**: Create `tests/UPACIP.Tests.Common/UPACIP.Tests.Common.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <IsPackable>false</IsPackable>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Moq" Version="4.*" />
       <PackageReference Include="FluentAssertions" Version="6.*" />
       <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.*" />
       <PackageReference Include="Bogus" Version="35.*" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="..\..\src\UPACIP.Contracts\UPACIP.Contracts.csproj" />
       <ProjectReference Include="..\..\src\UPACIP.Service\UPACIP.Service.csproj" />
       <ProjectReference Include="..\..\src\UPACIP.DataAccess\UPACIP.DataAccess.csproj" />
     </ItemGroup>
   </Project>
   ```
   Packages:
   - `Moq 4.x` — dependency mocking.
   - `FluentAssertions 6.x` — readable assertion syntax.
   - `Microsoft.EntityFrameworkCore.InMemory 8.x` — in-memory DbContext for isolated database tests.
   - `Bogus 35.x` — test data generation for realistic entity creation.

2. **Create `DbContextFixture` for in-memory database tests**: Create in `tests/UPACIP.Tests.Common/Fixtures/DbContextFixture.cs`:
   ```csharp
   public static class DbContextFixture
   {
       public static ApplicationDbContext CreateInMemory(string? dbName = null)
       {
           var options = new DbContextOptionsBuilder<ApplicationDbContext>()
               .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
               .Options;
           var context = new ApplicationDbContext(options);
           context.Database.EnsureCreated();
           return context;
       }
   }
   ```
   Each test gets a unique in-memory database by default, ensuring test isolation. Named databases can be shared across test methods via the `dbName` parameter when testing multi-step workflows.

3. **Create mock factories for external services (edge case 1)**: Create in `tests/UPACIP.Tests.Common/Mocks/`:

   **`MockAiGatewayFactory.cs`** — Mock/stub for AI gateway service dependencies:
   ```csharp
   public static class MockAiGatewayFactory
   {
       public static Mock<IAiGatewayService> CreateDefault()
       {
           var mock = new Mock<IAiGatewayService>();
           mock.Setup(x => x.ProcessAsync(It.IsAny<AiRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AiResponse { Success = true, ConfidenceScore = 0.95 });
           return mock;
       }

       public static Mock<IAiGatewayService> CreateFailure(string errorMessage = "AI service unavailable")
       {
           var mock = new Mock<IAiGatewayService>();
           mock.Setup(x => x.ProcessAsync(It.IsAny<AiRequest>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException(errorMessage));
           return mock;
       }

       public static Mock<IAiGatewayService> CreateLowConfidence(double confidence = 0.5)
       {
           var mock = new Mock<IAiGatewayService>();
           mock.Setup(x => x.ProcessAsync(It.IsAny<AiRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AiResponse { Success = true, ConfidenceScore = confidence });
           return mock;
       }
   }
   ```

   **`MockExternalServiceFactory.cs`** — Mock/stub for SMS, email, external HTTP dependencies:
   ```csharp
   public static class MockExternalServiceFactory
   {
       public static Mock<INotificationService> CreateNotificationService()
       {
           var mock = new Mock<INotificationService>();
           mock.Setup(x => x.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);
           mock.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);
           return mock;
       }

       public static Mock<IDistributedCache> CreateRedisCache()
       {
           var mock = new Mock<IDistributedCache>();
           var store = new Dictionary<string, byte[]>();
           mock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((string key, CancellationToken _) =>
                   store.TryGetValue(key, out var val) ? val : null);
           mock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
               .Callback((string key, byte[] val, DistributedCacheEntryOptions _, CancellationToken __) =>
                   store[key] = val);
           return mock;
       }
   }
   ```
   These factories address edge case 1 — tests never call real external services. Each factory provides `CreateDefault()` (happy path), `CreateFailure()` (error scenario), and scenario-specific variants.

4. **Create `UPACIP.Service.Tests` project**: Create `tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <IsPackable>false</IsPackable>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
       <PackageReference Include="xunit" Version="2.*" />
       <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
       <PackageReference Include="coverlet.collector" Version="6.*">
         <PrivateAssets>all</PrivateAssets>
         <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       </PackageReference>
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="..\UPACIP.Tests.Common\UPACIP.Tests.Common.csproj" />
       <ProjectReference Include="..\..\src\UPACIP.Service\UPACIP.Service.csproj" />
     </ItemGroup>
   </Project>
   ```
   Packages:
   - `Microsoft.NET.Test.Sdk 17.x` — test host.
   - `xunit 2.x` — test framework (AC-1).
   - `xunit.runner.visualstudio 2.x` — test discovery for Visual Studio and `dotnet test`.
   - `coverlet.collector 6.x` — code coverage instrumentation (AC-3).

5. **Create sample Service layer tests demonstrating conventions (AC-1)**: Create in `tests/UPACIP.Service.Tests/AuditLog/AuditLogCommandServiceTests.cs`:
   ```csharp
   public class AuditLogCommandServiceTests : IDisposable
   {
       private readonly ApplicationDbContext _dbContext;
       private readonly Mock<ILogger<AuditLogCommandService>> _loggerMock;
       private readonly AuditLogCommandService _sut;

       public AuditLogCommandServiceTests()
       {
           _dbContext = DbContextFixture.CreateInMemory();
           _loggerMock = new Mock<ILogger<AuditLogCommandService>>();
           _sut = new AuditLogCommandService(_dbContext, _loggerMock.Object);
       }

       [Fact]
       public async Task AppendAsync_ValidEntry_ReturnsNewId()
       {
           // Arrange
           var entry = new AuditLogEntry
           {
               UserId = Guid.NewGuid(),
               Action = "Create",
               EntityType = "Patient",
               EntityId = Guid.NewGuid()
           };

           // Act
           var result = await _sut.AppendAsync(entry);

           // Assert
           result.Should().NotBeEmpty();
           _dbContext.AuditLogs.Should().HaveCount(1);
       }

       [Theory]
       [InlineData("")]
       [InlineData(null)]
       public async Task AppendAsync_MissingAction_ThrowsValidationException(string? action)
       {
           // Arrange
           var entry = new AuditLogEntry
           {
               UserId = Guid.NewGuid(),
               Action = action!,
               EntityType = "Patient",
               EntityId = Guid.NewGuid()
           };

           // Act & Assert
           await Assert.ThrowsAsync<ArgumentException>(() => _sut.AppendAsync(entry));
       }

       [Fact]
       public async Task AppendBatchAsync_MultipleEntries_PersistsAll()
       {
           // Arrange
           var entries = Enumerable.Range(0, 5).Select(_ => new AuditLogEntry
           {
               UserId = Guid.NewGuid(),
               Action = "Access",
               EntityType = "Patient",
               EntityId = Guid.NewGuid()
           }).ToList();

           // Act
           await _sut.AppendBatchAsync(entries);

           // Assert
           _dbContext.AuditLogs.Should().HaveCount(5);
       }

       public void Dispose() => _dbContext.Dispose();
   }
   ```
   Conventions demonstrated:
   - `[Fact]` for single test cases, `[Theory]` with `[InlineData]` for parameterized tests.
   - Arrange/Act/Assert sections with comment markers.
   - `IDisposable` for DbContext cleanup.
   - FluentAssertions (`Should().NotBeEmpty()`, `Should().HaveCount()`).
   - `_sut` (System Under Test) naming convention.

6. **Create `UPACIP.Api.Tests` project**: Create `tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net8.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <IsPackable>false</IsPackable>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
       <PackageReference Include="xunit" Version="2.*" />
       <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
       <PackageReference Include="coverlet.collector" Version="6.*">
         <PrivateAssets>all</PrivateAssets>
         <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       </PackageReference>
       <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.*" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="..\UPACIP.Tests.Common\UPACIP.Tests.Common.csproj" />
       <ProjectReference Include="..\..\src\UPACIP.Api\UPACIP.Api.csproj" />
     </ItemGroup>
   </Project>
   ```
   Additional package: `Microsoft.AspNetCore.Mvc.Testing 8.x` — for `WebApplicationFactory<T>` integration testing of controllers.

7. **Create sample controller tests (AC-1)**: Create in `tests/UPACIP.Api.Tests/Controllers/AuditLogControllerTests.cs`:
   ```csharp
   public class AuditLogControllerTests
   {
       private readonly Mock<IAuditLogCommandService> _commandServiceMock;
       private readonly Mock<IAuditLogQueryService> _queryServiceMock;
       private readonly Mock<HateoasResponseWrapper> _wrapperMock;
       private readonly AuditLogController _sut;

       public AuditLogControllerTests()
       {
           _commandServiceMock = new Mock<IAuditLogCommandService>();
           _queryServiceMock = new Mock<IAuditLogQueryService>();
           _wrapperMock = new Mock<HateoasResponseWrapper>();
           _sut = new AuditLogController(
               _commandServiceMock.Object,
               _queryServiceMock.Object,
               _wrapperMock.Object);
       }

       [Fact]
       public async Task Create_ValidEntry_Returns201Created()
       {
           // Arrange
           var entry = new AuditLogEntry { Action = "Create", EntityType = "Patient", EntityId = Guid.NewGuid() };
           var expectedId = Guid.NewGuid();
           _commandServiceMock
               .Setup(x => x.AppendAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(expectedId);

           // Act
           var result = await _sut.Create(entry, CancellationToken.None);

           // Assert
           var createdResult = result.Should().BeOfType<CreatedAtRouteResult>().Subject;
           createdResult.StatusCode.Should().Be(201);
           _commandServiceMock.Verify(x => x.AppendAsync(entry, It.IsAny<CancellationToken>()), Times.Once);
       }

       [Fact]
       public async Task GetById_NonExistent_Returns404()
       {
           // Arrange
           _queryServiceMock
               .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((AuditLogReadModel?)null);

           // Act
           var result = await _sut.GetById(Guid.NewGuid(), CancellationToken.None);

           // Assert
           result.Should().BeOfType<NotFoundResult>();
       }
   }
   ```
   Demonstrates: Moq `Setup`/`Verify`, testing controller return types, mocking service interfaces from `UPACIP.Contracts`.

8. **Add test projects to solution and configure test settings**: Add all three test projects to `UPACIP.sln`. Create `tests/.runsettings` for shared test configuration:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <RunSettings>
     <DataCollectionRunSettings>
       <DataCollectors>
         <DataCollector friendlyName="XPlat Code Coverage">
           <Configuration>
             <Format>cobertura</Format>
             <Exclude>[*Tests*]*,[*Common*]*</Exclude>
             <ExcludeByFile>**/Migrations/**</ExcludeByFile>
             <SkipAutoProps>true</SkipAutoProps>
           </Configuration>
         </DataCollector>
       </DataCollectors>
     </DataCollectionRunSettings>
   </RunSettings>
   ```
   Coverage configuration:
   - Format: Cobertura XML (compatible with CI tools and ReportGenerator).
   - Exclude test assemblies and test common project from coverage metrics.
   - Exclude EF Core migrations from coverage.
   - `SkipAutoProps = true` — auto-properties don't count toward uncovered lines.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   └── Controllers/
│   ├── UPACIP.Contracts/                            ← from US_096 task_001
│   │   ├── UPACIP.Contracts.csproj
│   │   ├── Models/
│   │   └── Services/
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   └── ...
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── tests/
│   └── UPACIP.ArchTests/                            ← from US_096 task_001
├── Server/
├── app/
├── config/
└── scripts/
```

> Assumes US_001 (project scaffold) and US_096 task_001 (layered architecture with UPACIP.Contracts) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | tests/UPACIP.Tests.Common/UPACIP.Tests.Common.csproj | Shared test utilities: Moq, FluentAssertions, Bogus, EF InMemory |
| CREATE | tests/UPACIP.Tests.Common/Fixtures/DbContextFixture.cs | In-memory ApplicationDbContext factory for isolated tests |
| CREATE | tests/UPACIP.Tests.Common/Mocks/MockAiGatewayFactory.cs | Mock factory for AI gateway (happy, failure, low-confidence) |
| CREATE | tests/UPACIP.Tests.Common/Mocks/MockExternalServiceFactory.cs | Mock factory for SMS, email, Redis cache |
| CREATE | tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj | xUnit + Moq + coverlet test project for Service layer |
| CREATE | tests/UPACIP.Service.Tests/AuditLog/AuditLogCommandServiceTests.cs | Sample tests: Fact, Theory, FluentAssertions, Arrange/Act/Assert |
| CREATE | tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj | xUnit + Moq + MVC Testing project for Api layer |
| CREATE | tests/UPACIP.Api.Tests/Controllers/AuditLogControllerTests.cs | Sample controller tests with Moq Setup/Verify |
| CREATE | tests/.runsettings | Coverage config: Cobertura format, exclude tests/migrations |
| MODIFY | UPACIP.sln | Add 3 test projects to solution |

## External References

- [xUnit — Getting Started with .NET](https://xunit.net/docs/getting-started/netcore/cmdline)
- [Moq — Quickstart](https://github.com/devlooped/moq/wiki/Quickstart)
- [coverlet — Code Coverage for .NET](https://github.com/coverlet-coverage/coverlet)
- [FluentAssertions — Introduction](https://fluentassertions.com/introduction)
- [Unit Testing Best Practices — Microsoft](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

## Build Commands

```powershell
# Build all test projects
dotnet build tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj
dotnet build tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj

# Run unit tests
dotnet test tests/UPACIP.Service.Tests/UPACIP.Service.Tests.csproj
dotnet test tests/UPACIP.Api.Tests/UPACIP.Api.Tests.csproj

# Run with coverage
dotnet test --settings tests/.runsettings --collect:"XPlat Code Coverage"
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all test projects
- [ ] `dotnet test` discovers and executes xUnit tests (AC-1)
- [ ] Moq is available and functional for dependency mocking (AC-1)
- [ ] In-memory DbContext tests execute without external database dependency
- [ ] MockAiGatewayFactory provides happy, failure, and low-confidence scenarios (edge case 1)
- [ ] MockExternalServiceFactory provides SMS, email, and Redis stubs (edge case 1)
- [ ] coverlet.collector generates Cobertura XML coverage report (AC-3)
- [ ] Test assemblies and migrations are excluded from coverage metrics
- [ ] Sample tests follow Arrange/Act/Assert, [Fact]/[Theory] conventions
- [ ] Controller tests use Moq Setup/Verify for service interface mocking

## Implementation Checklist

- [ ] Create UPACIP.Tests.Common project with Moq, FluentAssertions, Bogus, EF InMemory
- [ ] Implement DbContextFixture with in-memory database factory
- [ ] Create MockAiGatewayFactory with default/failure/low-confidence variants
- [ ] Create MockExternalServiceFactory with SMS, email, Redis mock builders
- [ ] Create UPACIP.Service.Tests project with xUnit, Moq, coverlet
- [ ] Write sample AuditLogCommandServiceTests with Fact, Theory, FluentAssertions
- [ ] Create UPACIP.Api.Tests project with xUnit, Moq, MVC Testing
- [ ] Write sample AuditLogControllerTests with Moq Setup/Verify patterns
