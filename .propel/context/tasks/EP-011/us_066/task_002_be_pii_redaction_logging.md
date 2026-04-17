# Task - task_002_be_pii_redaction_logging

## Requirement Reference

- User Story: us_066
- Story Location: .propel/context/tasks/EP-011/us_066/us_066.md
- Acceptance Criteria:
    - AC-2: **Given** application logs are written, **When** patient data (names, DOB, email, phone, SSN) appears in the context, **Then** all PII is redacted with masked patterns (e.g., "j***@e***.com") before logging.
- Requirement Tags: NFR-017
- Edge Case:
    - EC-2: PII in structured logging (JSON) — PII fields are identified by config-driven field names and redacted at the Serilog sink level before writing.

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
| Logging | Serilog + Seq (Community Edition) | 8.x / 2024.x |
| Logging Integration | Serilog.AspNetCore | 8.x |
| Testing | xUnit + Moq | 2.x / 4.x |

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

Implement a PII redaction layer within the Serilog logging pipeline that automatically masks personally identifiable information (names, DOB, email, phone, SSN) before log events are written to any sink (Seq, file, console). The solution uses a combination of a custom Serilog `ILogEventEnricher` for message template redaction and a custom `IDestructuringPolicy` for structured object property redaction. PII field identification is config-driven via `appsettings.json`, enabling field names to be added or removed without code changes. The `Serilog.Enrichers.Sensitive` community package is evaluated but a custom implementation is preferred for full control over masking patterns specific to healthcare data (HIPAA compliance).

## Dependent Tasks

- US_007 — Foundational — Requires Serilog logging configuration to be established (Serilog + Seq pipeline in Program.cs)

## Impacted Components

- **NEW** — `Server/Logging/PiiRedactionEnricher.cs` — Serilog enricher that redacts PII from log event message templates and scalar properties
- **NEW** — `Server/Logging/PiiDestructuringPolicy.cs` — Serilog destructuring policy that redacts PII from structured object properties
- **NEW** — `Server/Logging/PiiMaskingPatterns.cs` — Static utility class with masking algorithms for each PII type
- **NEW** — `Server/Configuration/PiiRedactionOptions.cs` — Strongly-typed options class for PII field configuration
- **MODIFY** — `Server/Program.cs` — Register PII enricher and destructuring policy in Serilog pipeline
- **MODIFY** — `Server/appsettings.json` — Add PII redaction configuration section

## Implementation Plan

1. **Create `PiiRedactionOptions` configuration class**:
   - `PiiFieldNames` — List of property names to treat as PII (e.g., `["Email", "PhoneNumber", "Ssn", "DateOfBirth", "FirstName", "LastName", "PatientName"]`).
   - `PiiPatterns` — Dictionary mapping regex patterns to PII types for auto-detection in unstructured strings (e.g., email regex, SSN regex, phone regex).
   - Bind from `appsettings.json` section `"PiiRedaction"` using `IOptions<PiiRedactionOptions>`.
   - Config-driven approach satisfies EC-2: field names are identified by configuration, not hardcoded.

2. **Create `PiiMaskingPatterns` utility class** with static masking methods:
   - `MaskEmail(string email)` → `"j***@e***.com"` — Keeps first char of local part, first char of domain, and TLD.
   - `MaskPhone(string phone)` → `"***-***-1234"` — Keeps last 4 digits only.
   - `MaskSsn(string ssn)` → `"***-**-6789"` — Keeps last 4 digits only.
   - `MaskName(string name)` → `"J***"` — Keeps first character, masks remainder.
   - `MaskDateOfBirth(string dob)` → `"****/01/****"` — Masks year and day, keeps month.
   - `MaskGeneric(string value)` → `"****"` — Full mask for unrecognized PII fields.
   - Each method handles null/empty input gracefully (returns empty string).

3. **Create `PiiRedactionEnricher`** implementing `ILogEventEnricher`:
   - Injects `IOptions<PiiRedactionOptions>` via constructor.
   - In `Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)`:
     - Iterates through `logEvent.Properties`.
     - For each property whose name matches a configured PII field name (case-insensitive), replaces the value with the masked version using `PiiMaskingPatterns`.
     - Detects PII type from field name suffix (e.g., fields ending in "Email" use `MaskEmail`, "Phone" use `MaskPhone`, etc.).
     - For string properties matching PII regex patterns (from `PiiPatterns`), applies corresponding mask.

4. **Create `PiiDestructuringPolicy`** implementing `IDestructuringPolicy`:
   - Handles structured objects logged with `{@Object}` destructuring operator.
   - In `TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)`:
     - Reflects over public properties of the object.
     - For properties matching configured PII field names, replaces values with masked versions.
     - Returns the restructured object with PII fields masked.
   - This satisfies EC-2: PII in structured JSON logging is redacted at the Serilog pipeline level before writing to any sink.

5. **Register in `Program.cs`**:
   - `builder.Services.Configure<PiiRedactionOptions>(builder.Configuration.GetSection("PiiRedaction"))`.
   - Add to Serilog configuration: `.Enrich.With(new PiiRedactionEnricher(piiOptions))`.
   - Add destructuring: `.Destructure.With(new PiiDestructuringPolicy(piiOptions))`.
   - Registration must occur before any sink configuration to ensure all sinks receive redacted data.

6. **Add configuration to `appsettings.json`**:
   ```json
   "PiiRedaction": {
     "PiiFieldNames": ["Email", "PhoneNumber", "Ssn", "SocialSecurityNumber", "DateOfBirth", "Dob", "FirstName", "LastName", "PatientName", "FullName"],
     "PiiPatterns": {
       "Email": "^[\\w.-]+@[\\w.-]+\\.\\w+$",
       "Phone": "^\\+?\\d[\\d\\s()-]{7,}$",
       "Ssn": "^\\d{3}-?\\d{2}-?\\d{4}$"
     }
   }
   ```

## Current Project State

```text
Server/
├── Program.cs
├── appsettings.json
├── Logging/
│   └── (empty — to be created)
├── Configuration/
│   └── (empty — to be created)
└── Server.csproj
```

> Project structure is a placeholder — will be updated based on completion of dependent task US_007.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Logging/PiiRedactionEnricher.cs | Serilog ILogEventEnricher that masks PII in scalar log properties |
| CREATE | Server/Logging/PiiDestructuringPolicy.cs | Serilog IDestructuringPolicy that masks PII in destructured objects |
| CREATE | Server/Logging/PiiMaskingPatterns.cs | Static utility with masking algorithms per PII type (email, phone, SSN, name, DOB) |
| CREATE | Server/Configuration/PiiRedactionOptions.cs | Strongly-typed options for PII field names and detection patterns |
| MODIFY | Server/Program.cs | Register PiiRedactionEnricher and PiiDestructuringPolicy in Serilog pipeline |
| MODIFY | Server/appsettings.json | Add "PiiRedaction" configuration section with field names and patterns |

## External References

- [Serilog Enrichment Documentation](https://github.com/serilog/serilog/wiki/Enrichment)
- [Serilog Structured Data — Destructuring](https://github.com/serilog/serilog/wiki/Structured-Data)
- [Serilog.Enrichers.Sensitive — Community Package](https://github.com/serilog-contrib/Serilog.Enrichers.Sensitive)
- [Serilog.AspNetCore Integration](https://github.com/serilog/serilog-aspnetcore)
- [HIPAA Safe Harbor De-identification](https://www.hhs.gov/hipaa/for-professionals/privacy/special-topics/de-identification/index.html)
- [ASP.NET Core Options Pattern (.NET 8)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0)

## Build Commands

```powershell
cd Server
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Email addresses in log output are masked as `"j***@e***.com"` pattern
- [ ] Phone numbers in log output are masked as `"***-***-1234"` pattern
- [ ] SSN values in log output are masked as `"***-**-6789"` pattern
- [ ] Patient names in log output are masked as `"J***"` pattern
- [ ] DOB values in log output are masked with year/day redacted
- [ ] Structured objects logged with `{@Object}` have PII fields masked (EC-2)
- [ ] Adding a new PII field name to `appsettings.json` triggers redaction without code changes
- [ ] Non-PII fields in log output remain unmasked

## Implementation Checklist

- [ ] Create `PiiRedactionOptions` with `PiiFieldNames` and `PiiPatterns` properties, bound to `appsettings.json`
- [ ] Create `PiiMaskingPatterns` with `MaskEmail()`, `MaskPhone()`, `MaskSsn()`, `MaskName()`, `MaskDateOfBirth()`, and `MaskGeneric()` methods
- [ ] Create `PiiRedactionEnricher` implementing `ILogEventEnricher` with config-driven field matching and pattern-based detection
- [ ] Create `PiiDestructuringPolicy` implementing `IDestructuringPolicy` for structured object PII redaction
- [ ] Register enricher and destructuring policy in Serilog pipeline in `Program.cs`
- [ ] Add `PiiRedaction` configuration section to `appsettings.json` with default PII field names and regex patterns
