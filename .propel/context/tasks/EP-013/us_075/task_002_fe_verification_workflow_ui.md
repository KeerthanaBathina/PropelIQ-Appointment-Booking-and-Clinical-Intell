# Task - task_002_fe_verification_workflow_ui

## Requirement Reference

- User Story: us_075
- Story Location: .propel/context/tasks/EP-013/us_075/us_075.md
- Acceptance Criteria:
  - AC-1: Given AI generates medical codes (ICD-10/CPT), When the codes are created, Then they remain in "pending-verification" status and cannot be finalized without staff approval.
  - AC-2: Given AI extracts clinical data, When confidence is below 0.80, Then the data is blocked from entering the consolidated profile until a staff member verifies it.
  - AC-3: Given a staff member verifies AI output, When they approve or modify the data, Then the verification is logged with staff ID, timestamp, original AI value, and final value.
  - AC-4: Given the human-in-the-loop rule is configured, When any attempt is made to bypass verification (e.g., via API), Then the system rejects the operation with "verification required" error.
- Edge Case:
  - What happens when no staff member is available to verify time-sensitive results? System queues results in "pending" status indefinitely; there is no auto-approval mechanism for clinical data.
  - How does the system handle verification for bulk AI outputs (50+ items)? System provides batch verification UI with "approve all" option that records individual audit entries for each item.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-014-medical-coding.html` |
| **Screen Spec** | figma_spec.md#SCR-014 |
| **UXR Requirements** | UXR-105, UXR-605 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**IF Wireframe Status = AVAILABLE or EXTERNAL:**

- **MUST** open and reference the wireframe file/URL during UI implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe
- **MUST** implement all states shown in wireframe (default, hover, focus, error, loading)
- **MUST** validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| UI Component Library | Material-UI (MUI) | 5.x |
| State Management | React Query | 4.x |
| State Management | Zustand | 4.x |

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

Implement the frontend verification workflow UI on the Medical Coding Review screen (SCR-014) that enforces human-in-the-loop verification for all AI-generated medical outputs. The UI presents pending AI outputs in a verification queue with status badges, confidence scores (UXR-105 color-coded), approve/modify/reject actions per item, a batch "approve all" option for 50+ items (edge case), an override justification dialog, and verification audit trail display. The UI blocks finalization workflows until all required items are verified and displays "verification required" error states when bypass is attempted. Handles AI service unavailability with manual workflow fallback per UXR-605.

## Dependent Tasks

- US_075 task_001_be_verification_enforcement — Requires backend verification API endpoints (approve, modify, reject, batch approve).
- US_049 — Requires code verification workflow foundation on SCR-014.

## Impacted Components

- **NEW** `app/src/features/coding/verification/VerificationQueue.tsx` — Main verification queue container with pending items list and batch actions
- **NEW** `app/src/features/coding/verification/components/VerificationCard.tsx` — Individual verification item card with status badge, confidence score, approve/modify/reject actions
- **NEW** `app/src/features/coding/verification/components/OverrideJustificationDialog.tsx` — MUI Dialog for code override with justification text field and code search
- **NEW** `app/src/features/coding/verification/components/BatchVerificationToolbar.tsx` — Toolbar with "Approve All" button, selection checkboxes, progress indicator
- **NEW** `app/src/features/coding/verification/components/VerificationAuditTrail.tsx` — Expandable panel showing verification history per item
- **NEW** `app/src/features/coding/verification/hooks/useVerification.ts` — React Query hooks for verification API calls
- **NEW** `app/src/features/coding/verification/types.ts` — TypeScript interfaces for verification DTOs
- **MODIFY** `app/src/features/coding/MedicalCodingReview.tsx` — Integrate verification queue into SCR-014 layout

## Implementation Plan

1. **Define TypeScript interfaces**: Create types matching backend DTOs: `VerificationItem` (id, recordType, codeValue, description, confidenceScore, verificationStatus, aiJustification, sourceAttribution), `VerificationRequest` (recordId, action, newValue?, justification?), `BatchVerificationRequest` (recordIds, recordType), `VerificationAuditEntry` (staffId, staffName, timestamp, originalValue, finalValue, action, justification). Define `VerificationStatus` union type: `'pending-verification' | 'verified' | 'modified' | 'rejected'`.

2. **Create React Query hooks**: Implement `useVerificationQueue(patientId)` hook calling `GET /api/staff/verification/pending?patientId=` to fetch pending items. Implement `useApproveVerification()` mutation calling `POST /api/staff/verification/approve`. Implement `useModifyVerification()` mutation calling `POST /api/staff/verification/modify`. Implement `useRejectVerification()` mutation calling `POST /api/staff/verification/reject`. Implement `useBatchApprove()` mutation calling `POST /api/staff/verification/batch-approve`. All mutations invalidate the verification queue query on success.

3. **Build `VerificationCard` component**: MUI `Card` displaying: code value and description as primary text, AI justification as secondary text, confidence score as color-coded `Chip` (green ≥ 0.90, amber 0.80-0.89, red < 0.80) per UXR-105, verification status `Badge` ("Pending" amber, "Verified" green, "Modified" blue, "Rejected" red), three action buttons — `Button variant="contained"` "Approve", `Button variant="outlined"` "Modify", `Button variant="text" color="error"` "Reject". Disabled state for already-verified items.

4. **Build `OverrideJustificationDialog` component**: MUI `Dialog` triggered when staff clicks "Modify". Contains: current AI-suggested code displayed as read-only, `Autocomplete` code search field for replacement code (searches ICD-10/CPT library), `TextField multiline` for mandatory justification text (required, minimum 10 characters), "Confirm Override" `Button` submitting the modification, "Cancel" `Button`. Form validation prevents submission without justification per FR-066.

5. **Build `BatchVerificationToolbar` component**: Rendered above the verification queue when items > 1. Contains: `Checkbox` "Select All" toggle, selected count display (e.g., "3 of 12 selected"), `Button` "Approve Selected" (disabled when 0 selected), `LinearProgress` showing verification progress (e.g., "5/12 verified"). For 50+ items per edge case, the "Approve All" action shows a confirmation dialog: "You are about to approve {count} items. Each item will receive an individual audit entry. Continue?" to prevent accidental bulk approval.

6. **Build `VerificationAuditTrail` component**: MUI `Accordion` per verification item. Expandable panel showing chronological list of verification events: staff name, action taken (Approved/Modified/Rejected), timestamp, original AI value, final value (if modified), justification text (if override). Data fetched from verification audit API. Empty state: "No verification history yet."

7. **Compose `VerificationQueue` container**: Layout: `BatchVerificationToolbar` at top, scrollable list of `VerificationCard` components, `VerificationAuditTrail` expandable per card. Loading state with `Skeleton` placeholders per UXR-502. Error state with `Alert severity="error"` and retry button per UXR-601. Empty state when no pending items: `Alert severity="success"` "All AI outputs have been verified." AI unavailable state with banner: "AI service unavailable — switch to manual workflow" per UXR-605.

8. **Integrate into `MedicalCodingReview` (SCR-014)**: Add `VerificationQueue` as a primary section within the medical coding review screen. The verification queue replaces the code finalization controls when pending items exist — finalization buttons are disabled with tooltip "Complete verification of all pending items before finalizing." Handle "verification required" 400 error from API by displaying inline `Alert`: "Human verification is required before this operation can be completed." per AC-4. Match wireframe layout at all breakpoints.

## Current Project State

```text
UPACIP/
├── app/
│   ├── package.json
│   ├── src/
│   │   ├── App.tsx
│   │   ├── features/
│   │   │   ├── coding/
│   │   │   │   └── MedicalCodingReview.tsx
│   │   │   ├── admin/
│   │   │   ├── auth/
│   │   │   └── patient/
│   │   ├── components/
│   │   │   └── shared/
│   │   └── hooks/
├── src/
│   ├── UPACIP.Api/
│   │   ├── Controllers/
│   │   └── Filters/
│   │       └── VerificationRequiredFilter.cs  ← from task_001
│   └── UPACIP.Service/
│       └── Verification/                      ← from task_001
└── scripts/
```

> Assumes task_001 (verification enforcement backend) and US_049 (code verification workflow) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/coding/verification/types.ts | TypeScript interfaces for VerificationItem, VerificationRequest, BatchVerificationRequest, VerificationAuditEntry |
| CREATE | app/src/features/coding/verification/hooks/useVerification.ts | React Query hooks: useVerificationQueue, useApproveVerification, useModifyVerification, useRejectVerification, useBatchApprove |
| CREATE | app/src/features/coding/verification/components/VerificationCard.tsx | Item card with status badge, confidence chip (UXR-105), approve/modify/reject buttons |
| CREATE | app/src/features/coding/verification/components/OverrideJustificationDialog.tsx | Override dialog with code search, justification text, confirmation |
| CREATE | app/src/features/coding/verification/components/BatchVerificationToolbar.tsx | Select all, approve selected, progress indicator, 50+ item confirmation |
| CREATE | app/src/features/coding/verification/components/VerificationAuditTrail.tsx | Accordion-style verification history per item with staff attribution |
| CREATE | app/src/features/coding/verification/VerificationQueue.tsx | Container: toolbar, card list, loading/error/empty/AI-unavailable states |
| MODIFY | app/src/features/coding/MedicalCodingReview.tsx | Integrate VerificationQueue, disable finalization until verification complete |

## External References

- [MUI Card API](https://mui.com/material-ui/react-card/)
- [MUI Dialog API](https://mui.com/material-ui/react-dialog/)
- [MUI Autocomplete API](https://mui.com/material-ui/react-autocomplete/)
- [MUI Accordion API](https://mui.com/material-ui/react-accordion/)
- [MUI Chip API](https://mui.com/material-ui/react-chip/)
- [React Query useMutation](https://tanstack.com/query/v4/docs/framework/react/reference/useMutation)
- [WCAG 2.1 AA Color Contrast](https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html)

## Build Commands

```powershell
# Install dependencies
cd app; npm install

# Build frontend
npm run build

# Run development server
npm run dev

# Run lint checks
npm run lint
```

## Implementation Validation Strategy

- [ ] `npm run build` completes with zero errors
- [ ] `npm run lint` passes with zero violations
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] VerificationCard shows color-coded confidence chips: green (≥0.90), amber (0.80-0.89), red (<0.80)
- [ ] OverrideJustificationDialog enforces mandatory justification text (min 10 chars)
- [ ] BatchVerificationToolbar shows confirmation dialog for 50+ item batch approval
- [ ] Finalization buttons disabled with tooltip when pending verification items exist
- [ ] "verification required" 400 error displays inline Alert message
- [ ] AI unavailable state shows fallback banner per UXR-605
- [ ] Loading state shows Skeleton placeholders per UXR-502
- [ ] Each batch-approved item creates individual audit entry (not a single bulk record)

## Implementation Checklist

- [ ] Define TypeScript interfaces in `types.ts`: `VerificationItem`, `VerificationRequest`, `BatchVerificationRequest`, `VerificationAuditEntry`, `VerificationStatus` union
- [ ] Create React Query hooks in `useVerification.ts`: queue fetch, approve/modify/reject mutations, batch approve mutation with query invalidation
- [ ] Build `VerificationCard` with confidence `Chip` (green/amber/red per UXR-105), status `Badge`, approve/modify/reject `Button` actions
- [ ] Build `OverrideJustificationDialog` with code search `Autocomplete`, justification `TextField` (required, min 10 chars), confirm/cancel buttons
- [ ] Build `BatchVerificationToolbar` with select-all, approve-selected, progress indicator, 50+ item confirmation dialog
- [ ] Build `VerificationAuditTrail` as expandable `Accordion` showing staff name, action, timestamp, original/final values
- [ ] Compose `VerificationQueue` container with loading/error/empty/AI-unavailable states
- [ ] Integrate into `MedicalCodingReview.tsx`: disable finalization when pending items exist, handle 400 "verification required" error
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
