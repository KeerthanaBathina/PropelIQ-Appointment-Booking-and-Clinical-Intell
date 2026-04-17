# Task - task_001_be_backup_encryption_service

## Requirement Reference

- User Story: us_089
- Story Location: .propel/context/tasks/EP-017/us_089/us_089.md
- Acceptance Criteria:
  - AC-1: Given a backup is created, When it is stored, Then the backup file is encrypted with AES-256 before being written to the storage location.
- Edge Case:
  - How does the system handle encryption key management for backups? Encryption keys are stored separately from backups using the system's key management configuration; key loss renders backups unrecoverable.

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

Implement a `BackupEncryptionService` that encrypts pg_dump backup files with AES-256 before they are written to the final storage location (DR-025). This service integrates into the existing `DatabaseBackupService` pipeline (from US_088) as a post-backup step: after `BackupExecutor` produces the plaintext `.dump` file, the encryption service reads it, encrypts using AES-256-CBC with a securely managed key, writes the encrypted output as a `.dump.enc` file, and deletes the plaintext original. The encryption key is stored separately from backups via the system's configuration — in `appsettings.json` for development and environment variables for production (edge case 2). The key is a 256-bit (32-byte) value configured as a Base64-encoded string. A random 128-bit IV is generated per encryption operation and prepended to the ciphertext so that decryption does not require separate IV storage. The service also provides a `DecryptBackupAsync` method for use by the restoration testing framework (US_089 task_003). The existing `BackupResult` and `BackupLog` are updated to reflect the encrypted file path, encrypted file size, and a checksum computed over the encrypted output (ensuring integrity verification covers the stored artifact).

## Dependent Tasks

- US_088 task_001_be_backup_orchestration_service — Requires DatabaseBackupService, BackupExecutor, BackupOptions, BackupResult, BackupLog.
- US_063 — Requires AES-256 encryption patterns and key management approach established for data-at-rest.

## Impacted Components

- **NEW** `src/UPACIP.Service/Backup/BackupEncryptionService.cs` — IBackupEncryptionService: AES-256 file encryption and decryption
- **NEW** `src/UPACIP.Service/Backup/Models/EncryptionOptions.cs` — Configuration: encryption key (Base64), algorithm parameters
- **MODIFY** `src/UPACIP.Service/Backup/DatabaseBackupService.cs` — Add post-backup encryption step after BackupExecutor
- **MODIFY** `src/UPACIP.Service/Backup/Models/BackupResult.cs` — Add EncryptedFilePath, EncryptedFileSizeBytes fields
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind EncryptionOptions, register IBackupEncryptionService
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add BackupEncryption configuration section

## Implementation Plan

1. **Create `EncryptionOptions` configuration model**: Create in `src/UPACIP.Service/Backup/Models/EncryptionOptions.cs`:
   - `string EncryptionKeyBase64` — Base64-encoded 256-bit (32-byte) AES key. Required; service throws `InvalidOperationException` at startup if missing or incorrect length.
   - `bool Enabled` (default: `true`) — allows disabling encryption for development environments.
   - `int BufferSizeBytes` (default: `81920` — 80 KB) — streaming read/write buffer for large backup files.
   Register via `IOptionsMonitor<EncryptionOptions>`. Add to `appsettings.json`:
   ```json
   "BackupEncryption": {
     "EncryptionKeyBase64": "",
     "Enabled": true,
     "BufferSizeBytes": 81920
   }
   ```
   The key value is left empty in `appsettings.json` (committed to source control) and provided via environment variable `BackupEncryption__EncryptionKeyBase64` in production. This ensures the key is never stored alongside backup files (edge case 2 — key separation).

2. **Implement `IBackupEncryptionService` / `BackupEncryptionService`**: Create in `src/UPACIP.Service/Backup/BackupEncryptionService.cs` with constructor injection of `IOptionsMonitor<EncryptionOptions>` and `ILogger<BackupEncryptionService>`.

   **Method `Task<EncryptionResult> EncryptBackupAsync(string plaintextFilePath, CancellationToken ct)`**:
   - (a) Validate the encryption key: decode from Base64, verify length is exactly 32 bytes (256 bits). If invalid, throw `InvalidOperationException("Encryption key must be exactly 256 bits")`.
   - (b) Generate a cryptographically random 128-bit IV using `RandomNumberGenerator.Fill(iv)`.
   - (c) Create `Aes` instance: `Aes.Create()` with `KeySize = 256`, `Mode = CipherMode.CBC`, `Padding = PaddingMode.PKCS7`. Set `Key` from decoded Base64, set `IV` from the generated value.
   - (d) Open the plaintext `.dump` file as a read stream.
   - (e) Create the output `.dump.enc` file (same directory, same name with `.enc` appended) as a write stream.
   - (f) Write the 16-byte IV as the first bytes of the encrypted file (allows decryption without separate IV storage).
   - (g) Create `CryptoStream` with the `ICryptoTransform` from `aes.CreateEncryptor()`, wrapping the output stream.
   - (h) Stream-copy the plaintext file through the `CryptoStream` using the configured buffer size to handle large backup files without loading the entire file into memory.
   - (i) Flush and close all streams.
   - (j) Compute SHA-256 checksum of the encrypted output file.
   - (k) Delete the plaintext `.dump` file after successful encryption — only the `.dump.enc` file is retained in storage.
   - (l) Return `EncryptionResult { EncryptedFilePath, EncryptedFileSizeBytes, Checksum, OriginalFileSizeBytes }`.
   - (m) Log: `Log.Information("BACKUP_ENCRYPTED: File={FileName}, OriginalSize={OriginalBytes}, EncryptedSize={EncryptedBytes}, Checksum={Checksum}")`.

   **Method `Task<string> DecryptBackupAsync(string encryptedFilePath, string outputPath, CancellationToken ct)`**:
   - (a) Validate the encryption key (same as above).
   - (b) Open the encrypted file as a read stream.
   - (c) Read the first 16 bytes as the IV.
   - (d) Create `Aes` instance with the same configuration, set `Key` and `IV`.
   - (e) Create `CryptoStream` with `aes.CreateDecryptor()`.
   - (f) Stream-copy through `CryptoStream` to the output file.
   - (g) Return the decrypted output file path.
   - (h) Log: `Log.Information("BACKUP_DECRYPTED: Source={EncryptedFile}, Output={DecryptedFile}")`.

3. **Define `EncryptionResult` DTO**: Create as a nested record or separate class:
   - `string EncryptedFilePath` — full path to the `.dump.enc` file.
   - `long EncryptedFileSizeBytes` — size of the encrypted file.
   - `string Checksum` — SHA-256 of the encrypted file.
   - `long OriginalFileSizeBytes` — size of the original plaintext file before encryption.

4. **Update `BackupResult` DTO**: Add to the existing `BackupResult` (from US_088 task_001):
   - `string? EncryptedFilePath` — path to the encrypted backup file (null if encryption disabled).
   - `long? EncryptedFileSizeBytes` — encrypted file size (null if encryption disabled).
   - `string? EncryptedChecksum` — SHA-256 of the encrypted file (replaces the plaintext checksum as the stored-file integrity value).

5. **Integrate encryption into `DatabaseBackupService`**: Modify the backup execution flow in `DatabaseBackupService` (from US_088 task_001). After a successful `BackupExecutor.ExecuteBackupAsync` call:
   ```csharp
   // After successful backup:
   if (encryptionOptions.CurrentValue.Enabled)
   {
       var encResult = await _encryptionService.EncryptBackupAsync(
           backupResult.FilePath, ct);
       backupResult = backupResult with
       {
           EncryptedFilePath = encResult.EncryptedFilePath,
           EncryptedFileSizeBytes = encResult.EncryptedFileSizeBytes,
           EncryptedChecksum = encResult.Checksum,
           // Update the primary file path to the encrypted file for downstream consumers
           FilePath = encResult.EncryptedFilePath,
           FileSizeBytes = encResult.EncryptedFileSizeBytes,
           Checksum = encResult.Checksum
       };
   }
   ```
   This ensures the `BackupLog` persisted downstream records the encrypted file metadata, and the retention service (US_088 task_002) operates on `.dump.enc` files. If encryption fails, the backup is still considered successful (the plaintext `.dump` file is preserved) but a warning is logged and the `BackupLog` records `Status = "CompletedUnencrypted"`.

6. **Secure key management practices**:
   - The encryption key is NEVER logged, even in debug/verbose mode. The `EncryptionOptions` class does NOT override `ToString()` to prevent accidental key exposure in structured logs.
   - The key is provided via `BackupEncryption__EncryptionKeyBase64` environment variable in production, keeping it separate from the backup storage volume (edge case 2).
   - If the key is rotated, previously encrypted backups remain decryptable only with the original key. Key rotation documentation notes this — key versioning is planned for Phase 2.
   - The key is validated at service startup via a hosted service health check: if the key is empty and `Enabled = true`, the service logs a critical error and refuses to start the backup cycle.

7. **Update retention service compatibility**: The retention service (US_088 task_002) scans for `upacip_backup_*.dump` files. After encryption, files will be named `upacip_backup_*.dump.enc`. Update the file scan pattern in `BackupRetentionService.ApplyRetentionPolicyAsync` to search for both `*.dump` and `*.dump.enc` patterns, ensuring backward compatibility with any pre-encryption backups.

8. **Register services and bind configuration**: In `Program.cs`: bind `EncryptionOptions` via `builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection("BackupEncryption"))`, register `services.AddSingleton<IBackupEncryptionService, BackupEncryptionService>()`.

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
│   │   │   └── Models/
│   │   │       ├── BackupOptions.cs                 ← from US_088 task_001
│   │   │       ├── BackupResult.cs                  ← from US_088 task_001
│   │   │       ├── BackupLog.cs                     ← from US_088 task_001
│   │   │       ├── BackupRetentionOptions.cs        ← from US_088 task_002
│   │   │       └── BackupFileInfo.cs                ← from US_088 task_002
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

> Assumes US_088 (backup orchestration and retention) and US_063 (AES-256 encryption) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Backup/BackupEncryptionService.cs | IBackupEncryptionService: AES-256-CBC file encryption and decryption with streaming I/O |
| CREATE | src/UPACIP.Service/Backup/Models/EncryptionOptions.cs | Config: encryption key (Base64), enabled flag, buffer size |
| MODIFY | src/UPACIP.Service/Backup/DatabaseBackupService.cs | Add post-backup encryption step, handle encryption failure gracefully |
| MODIFY | src/UPACIP.Service/Backup/Models/BackupResult.cs | Add EncryptedFilePath, EncryptedFileSizeBytes, EncryptedChecksum |
| MODIFY | src/UPACIP.Service/Backup/BackupRetentionService.cs | Update file scan pattern to include *.dump.enc files |
| MODIFY | src/UPACIP.Api/Program.cs | Bind EncryptionOptions, register IBackupEncryptionService |
| MODIFY | src/UPACIP.Api/appsettings.json | Add BackupEncryption configuration section |

## External References

- [Aes Class — .NET Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes)
- [CryptoStream — .NET Streaming Encryption](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptostream)
- [RandomNumberGenerator — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator)
- [AES-256 Best Practices — OWASP Cryptographic Storage](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html)
- [.NET Configuration Environment Variables](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/#environment-variables)

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
- [ ] Backup file is encrypted with AES-256 and output has `.dump.enc` extension (AC-1)
- [ ] Plaintext `.dump` file is deleted after successful encryption
- [ ] Encrypted file can be decrypted back to match the original plaintext backup (round-trip)
- [ ] Random IV is prepended to the encrypted file (unique per encryption operation)
- [ ] Encryption key is never logged or visible in process arguments
- [ ] Empty encryption key with `Enabled = true` prevents backup cycle start
- [ ] Encryption failure does not fail the backup — plaintext file preserved with warning
- [ ] Retention service scans both `.dump` and `.dump.enc` file patterns
- [ ] BackupLog records encrypted file metadata (size, checksum, path)

## Implementation Checklist

- [ ] Create `EncryptionOptions` with Base64 key, enabled flag, and buffer size
- [ ] Implement `IBackupEncryptionService` with streaming AES-256-CBC encryption
- [ ] Implement `DecryptBackupAsync` for restoration testing support
- [ ] Define `EncryptionResult` DTO with encrypted file metadata
- [ ] Update `BackupResult` with encrypted file path, size, and checksum fields
- [ ] Integrate encryption into `DatabaseBackupService` post-backup pipeline
- [ ] Update `BackupRetentionService` file scan pattern for `.dump.enc` compatibility
- [ ] Register EncryptionOptions and IBackupEncryptionService in Program.cs
