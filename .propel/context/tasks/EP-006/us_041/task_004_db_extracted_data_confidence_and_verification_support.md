# Task - task_004_db_extracted_data_confidence_and_verification_support

## Requirement Reference

- User Story: US_041
- Story Location: .propel/context/tasks/EP-006/us_041/us_041.md
- Acceptance Criteria:
    - AC-1: Given AI extraction produces data points, When each data point is stored, Then a confidence score (0.00-1.00) is calculated and saved with the ExtractedData record.
    - AC-2: Given an extracted data point has confidence below 0.80, When the results are displayed, Then the data point is visually flagged with an amber/red indicator and marked for mandatory manual verification.
    - AC-4: Given a data point is flagged for verification, When the staff member confirms or corrects it, Then the verification status updates to "verified" with staff attribution and timestamp.
- Edge Case:
    - EC-1: If the AI cannot assign a confidence score, the schema must persist the `confidence-unavailable` review reason while defaulting the score safely to `0.00`.
    - EC-2: If staff bulk-verify flagged rows, the schema and indexes must support efficient multi-row updates and subsequent retrieval of remaining review work.

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

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

Extend the `ExtractedData` persistence model to support confidence-review workflows cleanly. This database task keeps the existing `ConfidenceScore`, `FlaggedForReview`, and `VerifiedByUserId` baseline, then adds the missing verification status, verification timestamp, and structured review-reason support needed for low-confidence and `confidence-unavailable` cases. The schema must remain backward-compatible for previously extracted rows while adding indexes that make flagged-row retrieval and bulk verification updates efficient for SCR-012 and SCR-013.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models (`ExtractedData` baseline confidence and verifier fields must exist)
- US_008 task_002_be_efcore_configuration_migrations (`ExtractedData` EF mapping baseline must exist)
- US_040 task_003_db_extracted_data_attribution_and_document_status_support (Current extracted-data attribution and document-status support must exist)

## Impacted Components

- **MODIFY** `ExtractedData` entity - Add verification status, verification timestamp, and structured review-reason support while preserving existing confidence fields (src/UPACIP.DataAccess/Entities/)
- **MODIFY** extracted-data enums or value objects - Define `VerificationStatus` and `ReviewReason` values needed by the verification workflow (src/UPACIP.DataAccess/Entities/Enums/ or shared model path)
- **MODIFY** `ExtractedDataConfiguration` - Configure new columns, enum conversions, and indexes for flagged-review retrieval and verification updates (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ApplicationDbContext` - Register any new enum or entity configuration changes (src/UPACIP.DataAccess/)
- **CREATE** EF Core migration - Add confidence-review support safely for existing rows (src/UPACIP.DataAccess/Migrations/)

## Implementation Plan

1. **Keep the existing confidence baseline intact** by retaining `ConfidenceScore`, `FlaggedForReview`, and `VerifiedByUserId` while extending the model only where US_041 introduces new persistence needs.
2. **Add verification-state fields** so extracted rows can move from pending review to verified with a persisted UTC timestamp rather than inferring status only from nullable verifier identity.
3. **Persist structured review reasons** so low-confidence and `confidence-unavailable` cases can be distinguished without overloading a generic boolean flag.
4. **Configure backward-compatible defaults** for existing rows, such as pending verification status, null verification timestamp, and null review reason when no review is required.
5. **Add indexes for review work queues** across flagged status, verification status, document ID, and confidence score so SCR-012 and SCR-013 can retrieve remaining review items efficiently.
6. **Generate a migration that supports bulk verification updates safely** without breaking existing extraction data or attribution fields added in US_040.

## Current Project State

```text
src/
  UPACIP.DataAccess/
    Entities/
      ExtractedData.cs
    Configurations/
      ExtractedDataConfiguration.cs
    ApplicationDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/Entities/ExtractedData.cs | Add verification status, verified-at timestamp, and structured review-reason fields |
| CREATE | src/UPACIP.DataAccess/Entities/Enums/VerificationStatus.cs | Define pending and verified states for extracted-data review |
| CREATE | src/UPACIP.DataAccess/Entities/Enums/ReviewReason.cs | Define low-confidence and `confidence-unavailable` review reasons |
| MODIFY | src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs | Configure enum conversions, defaults, and flagged-review indexes |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Register updated extracted-data mappings |
| CREATE | src/UPACIP.DataAccess/Migrations/AddExtractedDataConfidenceVerificationSupport.cs | EF Core migration for review-status, review-reason, and verification timestamp support |

## External References

- EF Core value conversions: https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- PostgreSQL partial indexes: https://www.postgresql.org/docs/current/indexes-partial.html

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Preserve the existing `ExtractedData` confidence baseline while adding only the new US_041 verification fields
- [x] Add persisted verification status and UTC verification timestamp support
- [x] Add structured review-reason support for low-confidence and `confidence-unavailable` cases
- [x] Configure safe defaults for existing extracted-data rows during migration
- [x] Add indexes that support flagged-row retrieval and efficient bulk verification updates
- [x] Keep the migration compatible with prior US_040 attribution changes