# Task - task_002_be_file_encryption_service

## Requirement Reference

- User Story: US_063
- Story Location: .propel/context/tasks/EP-011/us_063/us_063.md
- Acceptance Criteria:
    - AC-2: **Given** file storage contains clinical documents, **When** documents are stored on disk, **Then** they are encrypted with AES-256 and decrypted only during authorized access.
- Edge Case:
    - How does the system handle encryption key rotation? Phase 1 uses static encryption keys with documented rotation procedure; automated rotation planned for Phase 2.

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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

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

Implement an application-level AES-256 encryption service that encrypts clinical documents before writing them to disk and decrypts them only during authorized access. This service wraps all file I/O operations for the `ClinicalDocument` entity, ensuring that documents at rest are protected with AES-256-CBC encryption per HIPAA requirements (FR-091, NFR-009). Phase 1 uses static symmetric encryption keys stored in configuration with a documented key rotation procedure.

## Dependent Tasks

- US_001 - Foundational - Backend API scaffold must exist
- US_003 - Foundational - Database infrastructure and ClinicalDocument entity must exist

## Impacted Components

- `Server/Services/IFileEncryptionService.cs` — Encryption service interface (CREATE)
- `Server/Services/FileEncryptionService.cs` — AES-256-CBC encryption/decryption implementation (CREATE)
- `Server/Configuration/EncryptionOptions.cs` — Strongly-typed options for encryption key and IV management (CREATE)
- `Server/Services/DocumentStorageService.cs` — Integration point; modify to use `IFileEncryptionService` for file write/read (MODIFY)
- `Server/appsettings.json` — Add `Security:Encryption` configuration section (MODIFY)

## Implementation Plan

1. **Define encryption options model**: Create `EncryptionOptions` class with properties for `Key` (base64-encoded 256-bit key), `Algorithm` (default "AES-256-CBC"), and `KeyVersion` (integer for rotation tracking). Bind to `Security:Encryption` configuration section via `IOptions<EncryptionOptions>`.

2. **Create IFileEncryptionService interface**: Define `EncryptAsync(Stream plaintext, Stream ciphertext, CancellationToken ct)` and `DecryptAsync(Stream ciphertext, Stream plaintext, CancellationToken ct)` methods. Keep the interface transport-agnostic to support future storage backends.

3. **Implement FileEncryptionService**: Use `System.Security.Cryptography.Aes` with 256-bit key, CBC mode, and PKCS7 padding. Generate a random 16-byte IV per encryption operation and prepend it to the ciphertext stream. On decryption, read the first 16 bytes as IV, then decrypt the remainder. Validate key length (32 bytes) on initialization. Log encryption/decryption operations (document ID, success/failure) via Serilog without logging file content.

4. **Integrate with DocumentStorageService**: Modify the existing `DocumentStorageService` (or create if not yet exists) to call `IFileEncryptionService.EncryptAsync` before writing files to disk and `DecryptAsync` when reading files for authorized access. Ensure the encrypted file path uses the same naming convention with an `.enc` extension or metadata flag.

5. **Add configuration section**: Add `Security:Encryption` to `appsettings.json` with `Key` (placeholder for base64 key), `Algorithm`, and `KeyVersion`. Document in comments that the key must be generated via `openssl rand -base64 32` and stored securely. In production, reference key from Windows DPAPI or environment variable.

6. **Document key rotation procedure**: Add inline code comments and a configuration note describing the Phase 1 manual key rotation steps: generate new key, update `KeyVersion`, re-encrypt existing documents with new key using a migration script, and retire old key after verification.

7. **Register services in DI container**: Register `IFileEncryptionService` as singleton in `Program.cs`, bind `EncryptionOptions` from configuration.

## Current Project State

- Project structure is a placeholder; will be updated during execution based on completion of dependent tasks (US_001, US_003).

```
Server/
├── Program.cs
├── appsettings.json
├── Services/
│   └── DocumentStorageService.cs (expected from US_003 or related document upload story)
├── Configuration/
└── Models/
    └── ClinicalDocument.cs (entity from EP-DATA)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IFileEncryptionService.cs | Interface with EncryptAsync and DecryptAsync stream methods |
| CREATE | Server/Services/FileEncryptionService.cs | AES-256-CBC implementation with per-file random IV, key validation |
| CREATE | Server/Configuration/EncryptionOptions.cs | Strongly-typed options: Key, Algorithm, KeyVersion |
| MODIFY | Server/Services/DocumentStorageService.cs | Integrate IFileEncryptionService for encrypt-on-write, decrypt-on-read |
| MODIFY | Server/appsettings.json | Add `Security:Encryption` section with key placeholder and algorithm |
| MODIFY | Server/Program.cs | Register IFileEncryptionService and bind EncryptionOptions |

## External References

- [System.Security.Cryptography.Aes Class (.NET 8)](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes?view=net-8.0) — AES symmetric encryption API
- [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction?view=aspnetcore-8.0) — Data protection overview and key management
- [IOptions Pattern in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0) — Strongly-typed configuration binding
- [HIPAA Technical Safeguards (§164.312)](https://www.hhs.gov/hipaa/for-professionals/security/guidance/index.html) — Encryption requirements for ePHI at rest

## Build Commands

- `dotnet build Server/Server.csproj` — Compile backend project
- `dotnet test` — Run unit tests

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] AES-256-CBC encryption produces ciphertext different from plaintext
- [ ] Decryption of encrypted document returns byte-identical original content
- [ ] Random IV is unique per encryption operation (no IV reuse)
- [ ] Invalid key length (non-32-byte) throws descriptive exception on startup
- [ ] Encrypted files on disk are not readable without decryption
- [ ] Configuration loads correctly from `appsettings.json`

## Implementation Checklist

- [ ] Create `EncryptionOptions` class with Key, Algorithm, and KeyVersion properties
- [ ] Create `IFileEncryptionService` interface with EncryptAsync and DecryptAsync methods
- [ ] Implement `FileEncryptionService` using AES-256-CBC with random IV per operation
- [ ] Integrate encryption into `DocumentStorageService` for write and read operations
- [ ] Add `Security:Encryption` configuration section to `appsettings.json`
- [ ] Register encryption service and options in DI container (`Program.cs`)
- [ ] Log encryption operations via Serilog (document ID, success/failure only — no content)
- [ ] Document manual key rotation procedure in configuration comments
