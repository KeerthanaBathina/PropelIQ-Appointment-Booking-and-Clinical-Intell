# Task - task_002_be_geographic_backup_replication

## Requirement Reference

- User Story: us_089
- Story Location: .propel/context/tasks/EP-017/us_089/us_089.md
- Acceptance Criteria:
  - AC-2: Given encrypted backups exist, When they are stored, Then a copy is maintained in a geographically separate storage location from the primary database.
- Edge Case:
  - What happens when the geographic backup transfer fails? System retries 3 times with exponential backoff; if all fail, the primary backup is preserved and an alert is sent.

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
| Backend | Polly | 8.x |
| Backend | Serilog | 8.x |
| Infrastructure | Windows Server 2022 | - |

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

Implement a `BackupReplicationService` that copies encrypted backup files to a geographically separate storage location (DR-024). This service integrates into the `DatabaseBackupService` pipeline (from US_088) as a post-encryption step: after the encrypted `.dump.enc` file is produced by the `BackupEncryptionService` (from US_089 task_001), the replication service copies it to a configured remote destination. The remote destination is a UNC path to a Windows file share on a geographically separate server (e.g., `\\remote-server\BackupShare\Database\`). The service uses Polly 8.x retry policies with exponential backoff (3 retries: 30s, 120s, 480s delays) to handle transient network failures (edge case 1). If all three retries fail, the primary backup is preserved locally and a critical alert is emitted via Serilog — the backup itself is not considered failed, only the replication. The replication service verifies the copied file by comparing SHA-256 checksums between the local and remote copies to ensure data integrity during transfer. A `ReplicationLog` entry is persisted to the `BackupLog` table recording the replication status, destination path, transfer duration, and any error details.

## Dependent Tasks

- US_088 task_001_be_backup_orchestration_service — Requires DatabaseBackupService pipeline, BackupResult, BackupLog.
- US_089 task_001_be_backup_encryption_service — Requires encrypted backup files (.dump.enc) as input.

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/BackupReplicationService.cs` — IBackupReplicationService: geo-separate file copy with retry and verification
- **NEW** `src/UPACIP.Service/Backup/Models/ReplicationOptions.cs` — Configuration: remote destination, retry policy, verification
- **NEW** `src/UPACIP.Service/Backup/Models/ReplicationResult.cs` — DTO: replication status, destination, transfer duration
- **MODIFY** `src/UPACIP.Service/Backup/DatabaseBackupService.cs` — Add post-encryption replication step
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind ReplicationOptions, register IBackupReplicationService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add BackupReplication configuration section

## Implementation Plan

1. **Create `ReplicationOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/ReplicationOptions.cs`:
   - `string RemoteDestinationPath` — UNC path to the geographically separate file share (e.g., `"\\\\dr-server\\BackupShare\\Database"`). Required when `Enabled = true`.
   - `bool Enabled` (default: `true`) — allows disabling replication in development/test environments.
   - `int MaxRetries` (default: 3, per edge case 1 — "retries 3 times").
   - `int InitialRetryDelaySeconds` (default: 30) — first retry delay; subsequent retries use exponential backoff (30s → 120s → 480s).
   - `bool VerifyChecksum` (default: `true`) — whether to verify the remote copy checksum after transfer.
   - `int TransferTimeoutMinutes` (default: 60) — maximum time for a single file copy operation before timeout.
   Register via `IOptionsMonitor<ReplicationOptions>`. Add to `appsettings.json`:
   ```json
   "BackupReplication": {
     "RemoteDestinationPath": "",
     "Enabled": true,
     "MaxRetries": 3,
     "InitialRetryDelaySeconds": 30,
     "VerifyChecksum": true,
     "TransferTimeoutMinutes": 60
   }
   ```
   The remote path is left empty in `appsettings.json` and provided via environment variable `BackupReplication__RemoteDestinationPath` in production.

2. **Implement `IBackupReplicationService` / `BackupReplicationService`**: Create in `src/UPACIP.Service/Backup/BackupReplicationService.cs` with constructor injection of `IOptionsMonitor<ReplicationOptions>` and `ILogger<BackupReplicationService>`.

   **Method `Task<ReplicationResult> ReplicateBackupAsync(string sourceFilePath, CancellationToken ct)`**:
   - (a) Validate that `RemoteDestinationPath` is configured and the source file exists.
   - (b) Compute the destination file path: `Path.Combine(options.RemoteDestinationPath, Path.GetFileName(sourceFilePath))`.
   - (c) Ensure the remote destination directory exists: `Directory.CreateDirectory(options.RemoteDestinationPath)`. This handles UNC path directory creation on the remote share.
   - (d) Build a Polly `ResiliencePipeline` with retry strategy:
     ```csharp
     new ResiliencePipelineBuilder()
         .AddRetry(new RetryStrategyOptions
         {
             MaxRetryAttempts = options.MaxRetries,
             BackoffType = DelayBackoffType.Exponential,
             Delay = TimeSpan.FromSeconds(options.InitialRetryDelaySeconds),
             OnRetry = args =>
             {
                 Log.Warning(
                     "BACKUP_REPLICATION_RETRY: Attempt={Attempt}, Delay={Delay}s, Error={Error}",
                     args.AttemptNumber, args.RetryDelay.TotalSeconds,
                     args.Outcome.Exception?.Message);
                 return default;
             }
         })
         .Build();
     ```
   - (e) Execute the file copy within the Polly pipeline: use `File.Copy(sourceFilePath, destinationPath, overwrite: true)` for the copy operation. Measure transfer duration via `Stopwatch`.
   - (f) If `VerifyChecksum` is enabled: compute SHA-256 of both the local source and the remote destination, compare them. If mismatch, throw `IOException("Checksum verification failed")` to trigger a Polly retry.
   - (g) Return `ReplicationResult { Success = true, DestinationPath, TransferDuration, FileSizeBytes }`.
   - (h) Log: `Log.Information("BACKUP_REPLICATED: Source={Source}, Destination={Destination}, Size={SizeBytes}, Duration={Duration}")`.

3. **Handle replication failure (edge case 1)**: If all Polly retries are exhausted:
   - (a) Catch the final exception outside the Polly pipeline.
   - (b) Log a critical alert: `Log.Error("BACKUP_REPLICATION_FAILED: AllRetriesExhausted, Source={Source}, Destination={Destination}, Error={Error}. Primary backup preserved locally.")`.
   - (c) Return `ReplicationResult { Success = false, ErrorMessage = ... }`.
   - (d) The primary backup is NOT deleted — it remains in the local backup directory.
   - (e) The `DatabaseBackupService` treats replication failure as non-fatal: the backup cycle is still considered successful for logging and alerting purposes.

4. **Create `ReplicationResult` DTO**: Create in `src/UPACIP.Service/Backup/Models/ReplicationResult.cs`:
   - `bool Success` — true if the file was copied and checksum verified.
   - `string? DestinationPath` — full path on the remote share.
   - `long FileSizeBytes` — size of the copied file.
   - `TimeSpan TransferDuration` — elapsed time for the transfer.
   - `string? ErrorMessage` — error details on failure, null on success.
   - `int AttemptsUsed` — total attempts (1 = first try succeeded, up to 4 = all retries used).

5. **Integrate replication into `DatabaseBackupService`**: Modify the backup execution flow. After the encryption step (from task_001):
   ```csharp
   // After successful encryption:
   if (replicationOptions.CurrentValue.Enabled)
   {
       var replicationResult = await _replicationService.ReplicateBackupAsync(
           backupResult.FilePath, ct);
       if (!replicationResult.Success)
       {
           Log.Error("BACKUP_REPLICATION_FAILED: Backup preserved locally at {Path}",
               backupResult.FilePath);
       }
       // Persist replication status to BackupLog
   }
   ```
   The replication step runs after encryption so that only the encrypted `.dump.enc` file is sent to the remote location — plaintext backups never leave the primary server.

6. **Persist replication metadata to BackupLog**: After replication (success or failure), persist a `BackupLog` entry with:
   - `Status = "Replicated"` or `Status = "ReplicationFailed"`.
   - `FileName` — the encrypted backup filename.
   - `ErrorMessage` — replication error details (null on success).
   This reuses the existing `BackupLog` entity from US_088 with status values distinguishing backup, retention, encryption, and replication activities.

7. **Network credential handling**: UNC paths to remote Windows file shares may require authentication. The service runs under the Windows Service account identity (configured during deployment). If the service account has access to the remote share via domain credentials or a mapped share, no additional credential configuration is needed. If explicit credentials are required, `ReplicationOptions` can be extended with `RemoteUsername` and `RemotePassword` (encrypted) — but for Phase 1, domain-based access is assumed per the Windows Server 2022 deployment architecture.

8. **Register services and bind configuration**: In `Program.cs`: bind `ReplicationOptions` via `builder.Services.Configure<ReplicationOptions>(builder.Configuration.GetSection("BackupReplication"))`, register `services.AddSingleton<IBackupReplicationService, BackupReplicationService>()`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   │   ├── DatabaseBackupService.cs             ← from US_088 task_001
│   │   │   ├── BackupExecutor.cs                    ← from US_088 task_001
│   │   │   ├── BackupRetentionService.cs            ← from US_088 task_002
│   │   │   ├── BackupEncryptionService.cs           ← from US_089 task_001
│   │   │   └── Models/
│   │   │       ├── BackupOptions.cs                 ← from US_088 task_001
│   │   │       ├── BackupResult.cs                  ← from US_088 task_001 + US_089 task_001
│   │   │       ├── BackupLog.cs                     ← from US_088 task_001
│   │   │       ├── BackupRetentionOptions.cs        ← from US_088 task_002
│   │   │       ├── BackupFileInfo.cs                ← from US_088 task_002
│   │   │       └── EncryptionOptions.cs             ← from US_089 task_001
│   │   ├── Monitoring/
│   │   └── Retention/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_088 (backup orchestration/retention), US_063 (AES-256 encryption), and US_089 task_001 (backup encryption) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/BackupReplicationService.cs | IBackupReplicationService: geo-separate file copy with Polly retry and checksum verification |
| CREATE | src/UPACIP.Service/Backup/Models/ReplicationOptions.cs | Config: remote UNC path, retry count, backoff delay, checksum verification |
| CREATE | src/UPACIP.Service/Backup/Models/ReplicationResult.cs | DTO: replication status, destination, transfer duration, attempts |
| MODIFY | src/UPACIP.Service/Backup/DatabaseBackupService.cs | Add post-encryption replication step with non-fatal failure handling |
| MODIFY | src/UPACIP.Api/Program.cs | Bind ReplicationOptions, register IBackupReplicationService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add BackupReplication configuration section |

## External References

- [Polly v8 Retry Documentation](https://www.pollydocs.org/strategies/retry.html)
- [UNC Paths — Windows Networking](https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats#unc-paths)
- [File.Copy — .NET File Operations](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.copy)
- [SHA256 Checksum Verification — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)
- [Exponential Backoff Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Encrypted backup file is copied to the configured remote UNC path (AC-2)
- [ ] SHA-256 checksum of remote copy matches local source file
- [ ] Transient network failure triggers retry with exponential backoff (30s, 120s, 480s)
- [ ] All retries exhausted results in critical alert but primary backup preserved (edge case 1)
- [ ] Replication failure is non-fatal — backup cycle still succeeds
- [ ] Only encrypted `.dump.enc` files are replicated (plaintext never leaves primary server)
- [ ] BackupLog records replication status (Replicated / ReplicationFailed)
- [ ] Disabled replication (`Enabled = false`) skips the step without error
- [ ] Transfer timeout is enforced per `TransferTimeoutMinutes` configuration

## Implementation Checklist

- [ ] Create `ReplicationOptions` with remote UNC path, retry count, backoff delay, verification
- [ ] Implement `IBackupReplicationService` with file copy and Polly retry pipeline
- [ ] Implement post-transfer SHA-256 checksum verification
- [ ] Create `ReplicationResult` DTO with transfer status and metadata
- [ ] Handle all-retries-exhausted scenario with critical alert and primary backup preservation
- [ ] Integrate replication into `DatabaseBackupService` after encryption step
- [ ] Persist replication metadata to BackupLog table
- [ ] Register ReplicationOptions and IBackupReplicationService in Program.cs
