using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UPACIP.Api.Controllers;
using UPACIP.Contracts.Models;
using UPACIP.Service.Audit;
using UPACIP.Tests.Common.Mocks;
using Xunit;
using ContractsCommandService = UPACIP.Contracts.Services.IAuditLogCommandService;
using ContractsQueryService   = UPACIP.Contracts.Services.IAuditLogQueryService;
using ServiceQueryService     = UPACIP.Service.Audit.IAuditLogQueryService;

namespace UPACIP.Api.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="AuditLogController"/> covering the CQRS command and query
/// endpoints added in US_096/US_097 (AC-1, AC-2).
///
/// All six constructor dependencies are mocked so no HTTP pipeline is needed.
/// AI gateway coverage is provided by <see cref="MockAiGatewayFactory"/> stubs.
/// </summary>
public sealed class AuditLogControllerTests
{
    // ── Mocks ────────────────────────────────────────────────────────────────────────

    private readonly Mock<ServiceQueryService>          _serviceQueryMock = new();
    private readonly Mock<UPACIP.Service.Auth.IAuditLogService> _auditLogServiceMock = new();
    private readonly Mock<IClientInfoAccessor>          _clientInfoMock   = new();
    private readonly Mock<ContractsCommandService>      _commandMock      = new();
    private readonly Mock<ContractsQueryService>        _cqrsQueryMock    = new();

    private readonly AuditLogController _sut;

    public AuditLogControllerTests()
    {
        _sut = new AuditLogController(
            _serviceQueryMock.Object,
            _auditLogServiceMock.Object,
            _clientInfoMock.Object,
            NullLogger<AuditLogController>.Instance,
            _commandMock.Object,
            _cqrsQueryMock.Object);
    }

    // ── Create (POST /api/audit-logs) ─────────────────────────────────────────────────

    /// <summary>
    /// AC-1: A valid <see cref="AuditLogEntry"/> must be persisted and return HTTP 201 Created
    /// with a route reference to the newly created resource.
    /// </summary>
    [Fact]
    public async Task Create_ValidEntry_Returns201Created()
    {
        // Arrange
        var newId = Guid.NewGuid();
        var entry = new AuditLogEntry
        {
            Action     = "Login",
            EntityType = "User",
            EntityId   = Guid.NewGuid(),
        };

        _commandMock
            .Setup(x => x.AppendAsync(entry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        // Act
        var result = await _sut.Create(entry, CancellationToken.None);

        // Assert
        result.Should().BeOfType<CreatedAtRouteResult>();
        var created = (CreatedAtRouteResult)result;
        created.StatusCode.Should().Be(201);
        created.RouteName.Should().Be("GetAuditLogById");

        _commandMock.Verify(
            x => x.AppendAsync(entry, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GetById (GET /api/audit-logs/{id}) ───────────────────────────────────────────

    /// <summary>
    /// AC-2: A request for a non-existent audit log entry must return HTTP 404 Not Found.
    /// </summary>
    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        // Arrange
        var unknownId = Guid.NewGuid();
        _cqrsQueryMock
            .Setup(x => x.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLogReadModel?)null);

        // Act
        var result = await _sut.GetById(unknownId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    /// <summary>
    /// AC-2: A request for an existing entry must return HTTP 200 OK with the entry body.
    /// </summary>
    [Fact]
    public async Task GetById_ExistingEntry_Returns200WithBody()
    {
        // Arrange
        var id = Guid.NewGuid();
        var readModel = new AuditLogReadModel
        {
            Id           = id,
            TimestampUtc = DateTime.UtcNow,
            Action       = "Login",
            EntityType   = "User",
        };

        _cqrsQueryMock
            .Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(readModel);

        // Act
        var result = await _sut.GetById(id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result;
        ok.Value.Should().BeEquivalentTo(readModel);
    }
}
