/**
 * useManualIntakeForm — draft load, autosave, validation, and submit hook (US_028, SCR-009).
 *
 * Responsibilities:
 *   - Load an existing draft (or pre-filled AI data passed via navigation state).
 *   - Debounce autosave every 30 seconds (UXR-004 autosave requirement).
 *   - Provide inline validation within 200ms (UXR-501).
 *   - Handle submit and post-submit update flows.
 *   - Prevent duplicate submission (EC-2).
 *   - Restore last autosaved draft on return after navigation (EC-1).
 *
 * API contract (backed by task_002_be_manual_intake_api):
 *   GET  /api/intake/manual/draft         → load or restore draft
 *   POST /api/intake/manual/draft         → autosave partial data
 *   POST /api/intake/manual/submit        → final submission
 *
 * The hook also accepts `prefilledFields` from the AI intake handoff
 * (SwitchToManualResponse.prefilledFields dict), seeding the form
 * with AI-collected values and marking them as prefilled (AC-2).
 */

import { useState, useCallback, useRef, useEffect } from 'react';
import { apiPost, apiGet, ApiError } from '@/lib/apiClient';
import {
  useIntakeAutosave,
  type AutosaveStatus,
  AUTOSAVE_FAILED_MESSAGE,
  readLocalCacheDraft,
} from '@/hooks/useIntakeAutosave';
import type { InsurancePrecheckStatus } from '@/components/intake/InsurancePrecheckStatusBadge';

export { AUTOSAVE_FAILED_MESSAGE };
export type { AutosaveStatus };
export type { InsurancePrecheckStatus };

// ─── Form shape ───────────────────────────────────────────────────────────────

export interface ManualIntakeFormValues {
  // Personal Information
  firstName: string;
  lastName: string;
  dateOfBirth: string;       // ISO date string YYYY-MM-DD
  gender: string;
  phone: string;
  emergencyContact: string;

  // Medical History
  knownAllergies: string;
  currentMedications: string;
  preExistingConditions: string;

  // Insurance
  insuranceProvider: string;
  policyNumber: string;

  // Guardian Consent (US_031 AC-1) — shown only when patient age < 18
  guardianName: string;
  guardianDateOfBirth: string;  // ISO date string YYYY-MM-DD; used for EC-1 age guard
  guardianRelationship: string;
  guardianConsentAcknowledged: boolean;

  // Consent
  consentGiven: boolean;
}

export const EMPTY_FORM: ManualIntakeFormValues = {
  firstName: '',
  lastName: '',
  dateOfBirth: '',
  gender: '',
  phone: '',
  emergencyContact: '',
  knownAllergies: '',
  currentMedications: '',
  preExistingConditions: '',
  insuranceProvider: '',
  policyNumber: '',
  guardianName: '',
  guardianDateOfBirth: '',
  guardianRelationship: '',
  guardianConsentAcknowledged: false,
  consentGiven: false,
};

/** Maps AI field keys (from SwitchToManualResponse) to form field names. */
const AI_FIELD_MAP: Partial<Record<string, keyof ManualIntakeFormValues>> = {
  full_name:                  'firstName',   // split into first/last below
  contact_phone:              'phone',
  emergency_contact_name:     'emergencyContact',
  known_allergies:            'knownAllergies',
  current_medications:        'currentMedications',
  medical_history:            'preExistingConditions',
  insurance_provider:         'insuranceProvider',
  insurance_policy_number:    'policyNumber',
};

/** Always-required fields — shown with * in the form (FR-029). */
export const REQUIRED_FIELDS: Array<keyof ManualIntakeFormValues> = [
  'firstName',
  'lastName',
  'dateOfBirth',
  'gender',
  'phone',
  'knownAllergies',
  'consentGiven',
];

/** Additional required fields when the patient is under 18 (US_031 AC-1). */
export const GUARDIAN_REQUIRED_FIELDS: Array<keyof ManualIntakeFormValues> = [
  'guardianName',
  'guardianDateOfBirth',
  'guardianRelationship',
  'guardianConsentAcknowledged',
];

// ─── Age helpers ──────────────────────────────────────────────────────────────

/** Returns the age in whole years for an ISO YYYY-MM-DD date, or null when invalid. */
export function calcAgeYears(dob: string): number | null {
  if (!dob) return null;
  const d = new Date(dob);
  if (isNaN(d.getTime())) return null;
  const now = new Date();
  let age = now.getFullYear() - d.getFullYear();
  const m = now.getMonth() - d.getMonth();
  if (m < 0 || (m === 0 && now.getDate() < d.getDate())) age--;
  return age >= 0 ? age : null;
}

/** True when the DOB indicates the person is under 18. */
export function isUnder18(dob: string): boolean {
  const age = calcAgeYears(dob);
  return age !== null && age < 18;
}

export type FormErrors = Partial<Record<keyof ManualIntakeFormValues, string>>;

// ─── Hook state ───────────────────────────────────────────────────────────────

export type FormStatus =
  | 'idle'
  | 'loading'        // loading draft from backend
  | 'ready'          // form is editable
  | 'submitting'     // final submit in flight
  | 'submitted'      // successfully submitted
  | 'error';

interface DraftResponse {
  id?: string;
  fields: Partial<ManualIntakeFormValues>;
  lastSavedAt?: string;
}

interface SubmitResponse {
  intakeDataId: string;
  completedAt: string;
}

// ─── Validation ───────────────────────────────────────────────────────────────

const PHONE_RE = /^\(?(\d{3})\)?[-.\s]?(\d{3})[-.\s]?(\d{4})$/;

function validateField(
  field: keyof ManualIntakeFormValues,
  value: ManualIntakeFormValues[keyof ManualIntakeFormValues],
): string | undefined {
  if (field === 'consentGiven') {
    return (value as boolean) ? undefined : 'You must confirm consent to submit.';
  }

  const strValue = (value as string).trim();

  if (REQUIRED_FIELDS.includes(field) && !strValue) {
    const labels: Partial<Record<keyof ManualIntakeFormValues, string>> = {
      firstName:      'First name',
      lastName:       'Last name',
      dateOfBirth:    'Date of birth',
      gender:         'Gender',
      phone:          'Phone number',
      knownAllergies: 'Known allergies',
    };
    return `${labels[field] ?? field} is required.`;
  }

  if (field === 'phone' && strValue && !PHONE_RE.test(strValue)) {
    return 'Enter a valid US phone number (e.g., 555-123-4567).';
  }

  if (field === 'dateOfBirth' && strValue) {
    const dob = new Date(strValue);
    const now = new Date();
    if (isNaN(dob.getTime()) || dob > now) {
      return 'Enter a valid date of birth.';
    }
  }

  return undefined;
}

function validateAll(values: ManualIntakeFormValues): FormErrors {
  const errors: FormErrors = {};
  (Object.keys(values) as Array<keyof ManualIntakeFormValues>).forEach((key) => {
    const err = validateField(key, values[key]);
    if (err) errors[key] = err;
  });
  return errors;
}

// ─── Apply AI prefill mapping ─────────────────────────────────────────────────

function applyPrefillToForm(
  prefilled: Record<string, string>,
): Partial<ManualIntakeFormValues> {
  const result: Partial<ManualIntakeFormValues> = {};

  for (const [aiKey, value] of Object.entries(prefilled)) {
    if (aiKey === 'full_name' && value) {
      // Split "First Last" into firstName / lastName
      const parts = value.trim().split(/\s+/);
      result.firstName = parts[0] ?? '';
      result.lastName  = parts.slice(1).join(' ') || '';
    } else {
      const formKey = AI_FIELD_MAP[aiKey];
      if (formKey) {
        (result as Record<string, string>)[formKey as string] = value;
      }
    }
  }

  return result;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseManualIntakeFormOptions {
  /**
   * AI-collected prefilled values from SwitchToManualResponse.
   * Passed via `useLocation().state.prefilledFields` when navigating from AIIntakePage.
   */
  prefilledFields?: Record<string, string>;
}

export interface UseManualIntakeFormResult {
  values: ManualIntakeFormValues;
  errors: FormErrors;
  touched: Partial<Record<keyof ManualIntakeFormValues, boolean>>;
  /** Keys whose values came from AI intake prefill (AC-2 visual indicator) */
  prefilledKeys: Set<keyof ManualIntakeFormValues>;
  status: FormStatus;
  lastSavedAt: string | null;
  /** Transient autosave status from the shared hook (AC-3, EC-1 failure banner) */
  autosaveStatus: AutosaveStatus;
  errorMessage: string | null;
  /** True when patient DOB indicates age under 18 (US_031 AC-1). */
  isMinor: boolean;
  /** True when guardian DOB indicates the guardian is also under 18 (US_031 EC-1). */
  isGuardianAlsoMinor: boolean;
  /**
   * True when the patient is a minor AND guardian consent fields are not yet fully completed.
   * Used to show a booking-readiness warning and gate the BookingConfirmationModal (US_031 AC-1).
   */
  isGuardianConsentRequired: boolean;
  /** Soft insurance pre-check status (US_031 AC-2, AC-4, EC-2). */
  insurancePrecheckStatus: InsurancePrecheckStatus;
  /** Optional explanation from the insurance pre-check (shown when status = 'needs-review'). */
  insurancePrecheckMessage: string | null;
  handleChange: (field: keyof ManualIntakeFormValues, value: string | boolean) => void;
  handleBlur: (field: keyof ManualIntakeFormValues) => void;
  handleSubmit: () => Promise<void>;
  handleSaveDraft: () => Promise<void>;
}

export function useManualIntakeForm({
  prefilledFields,
}: UseManualIntakeFormOptions = {}): UseManualIntakeFormResult {
  const [values, setValues] = useState<ManualIntakeFormValues>(EMPTY_FORM);
  const [errors, setErrors] = useState<FormErrors>({});
  const [touched, setTouched] = useState<Partial<Record<keyof ManualIntakeFormValues, boolean>>>({});
  const [prefilledKeys, setPrefilledKeys] = useState<Set<keyof ManualIntakeFormValues>>(new Set());
  const [status, setStatus] = useState<FormStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Insurance pre-check state (US_031 AC-2, AC-4, EC-2)
  const [insurancePrecheckStatus, setInsurancePrecheckStatus] =
    useState<InsurancePrecheckStatus>('idle');
  const [insurancePrecheckMessage, setInsurancePrecheckMessage] = useState<string | null>(null);
  const insurancePrecheckTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Track if submission is already in flight (EC-2 dedup)
  const submittingRef = useRef(false);

  // Shared autosave hook (US_030, AC-1, AC-3, EC-1, EC-2)
  const { autosaveStatus, lastSavedAt, notifyChange, flush } = useIntakeAutosave({
    cacheKey:  'intake-manual-draft',
    enabled:   status === 'ready',
    saveThunk: async (snapshot) => {
      const r = await apiPost<{ lastSavedAt: string }>('/api/intake/manual/draft', {
        fields: snapshot as ManualIntakeFormValues,
      });
      return r.lastSavedAt;
    },
  });

  // ── Load draft on mount (EC-1 restore) ─────────────────────────────────────

  useEffect(() => {
    let cancelled = false;

    async function loadDraft() {
      try {
        const draft = await apiGet<DraftResponse>('/api/intake/manual/draft');

        if (!cancelled) {
          const merged = { ...EMPTY_FORM, ...draft.fields };

          // AC-2: reconcile a newer localStorage draft after network-loss interruption
          const localDraft = readLocalCacheDraft('intake-manual-draft');
          if (
            localDraft &&
            !localDraft.offlineOnly === false &&
            draft.lastSavedAt &&
            localDraft.savedAt > draft.lastSavedAt
          ) {
            // Local cache is newer — prefer it to recover offline edits (AC-2)
            Object.assign(merged, localDraft.snapshot as Partial<ManualIntakeFormValues>);
          }

          // Apply AI prefill on top of any stored draft (prefill wins for empty fields only)
          if (prefilledFields && Object.keys(prefilledFields).length > 0) {
            const prefillMapped = applyPrefillToForm(prefilledFields);
            const keys = new Set<keyof ManualIntakeFormValues>();

            (Object.keys(prefillMapped) as Array<keyof ManualIntakeFormValues>).forEach((k) => {
              // Only apply prefill if the field is empty in the draft
              if (!merged[k] || merged[k] === '' || merged[k] === false) {
                (merged as Record<string, unknown>)[k] = prefillMapped[k];
                keys.add(k);
              }
            });

            setPrefilledKeys(keys);
          }

          setValues(merged);
          if (draft.lastSavedAt) {
            // Seed the notifyChange ref so the hook knows there's a prior save timestamp
            notifyChange(merged);
          }
          setStatus('ready');
        }
      } catch (err) {
        if (!cancelled) {
          if (err instanceof ApiError && err.status === 404) {
            // No draft exists — start fresh, optionally with prefill
            let initial = { ...EMPTY_FORM };

            if (prefilledFields && Object.keys(prefilledFields).length > 0) {
              const prefillMapped = applyPrefillToForm(prefilledFields);
              const keys = new Set<keyof ManualIntakeFormValues>();
              (Object.keys(prefillMapped) as Array<keyof ManualIntakeFormValues>).forEach((k) => {
                (initial as Record<string, unknown>)[k] = prefillMapped[k];
                keys.add(k);
              });
              setPrefilledKeys(keys);
              initial = { ...initial, ...prefillMapped };
            }

            setValues(initial);
            setStatus('ready');
          } else {
            setErrorMessage('Failed to load your intake form. Please refresh and try again.');
            setStatus('error');
          }
        }
      }
    }

    loadDraft();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Autosave every 30s (UXR-004) ────────────────────────────────────────────
  // Notify the shared hook whenever values change so the 30-second boundary
  // save captures the latest state (EC-2: no intermediate keystroke spam).
  useEffect(() => {
    if (status !== 'ready') return;
    notifyChange(values);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [values, status]);

  // ── Insurance pre-check (US_031 AC-2, EC-2) ─────────────────────────────────
  // Debounce 800ms after insurance fields change; EC-2: if both are empty → 'skipped'.
  useEffect(() => {
    if (status !== 'ready') return;

    const { insuranceProvider, policyNumber } = values;
    const hasInsurance = insuranceProvider.trim() || policyNumber.trim();

    // EC-2: no insurance provided — skip pre-check
    if (!hasInsurance) {
      setInsurancePrecheckStatus('skipped');
      setInsurancePrecheckMessage(null);
      return;
    }

    // Both fields must be non-empty to run the check
    if (!insuranceProvider.trim() || !policyNumber.trim()) {
      setInsurancePrecheckStatus('idle');
      return;
    }

    if (insurancePrecheckTimerRef.current) {
      clearTimeout(insurancePrecheckTimerRef.current);
    }

    setInsurancePrecheckStatus('checking');

    insurancePrecheckTimerRef.current = setTimeout(async () => {
      try {
        const result = await apiPost<{ status: 'valid' | 'needs-review'; message?: string }>(
          '/api/intake/insurance/precheck',
          { insuranceProvider: insuranceProvider.trim(), policyNumber: policyNumber.trim() },
        );
        setInsurancePrecheckStatus(result.status);
        setInsurancePrecheckMessage(result.message ?? null);
      } catch {
        // Backend not yet available or transient error — silently reset (non-blocking)
        setInsurancePrecheckStatus('idle');
        setInsurancePrecheckMessage(null);
      }
    }, 800);

    return () => {
      if (insurancePrecheckTimerRef.current) {
        clearTimeout(insurancePrecheckTimerRef.current);
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [values.insuranceProvider, values.policyNumber, status]);

  // ── Derived minor / guardian state ──────────────────────────────────────────

  const isMinor = isUnder18(values.dateOfBirth);

  const isGuardianAlsoMinor =
    isMinor &&
    values.guardianDateOfBirth.trim() !== '' &&
    isUnder18(values.guardianDateOfBirth);

  const isGuardianConsentRequired =
    isMinor &&
    (!values.guardianName.trim() ||
      !values.guardianDateOfBirth.trim() ||
      !values.guardianRelationship.trim() ||
      !values.guardianConsentAcknowledged);

  // ── Field change handler ─────────────────────────────────────────────────────

  const handleChange = useCallback(
    (field: keyof ManualIntakeFormValues, value: string | boolean) => {
      setValues((prev) => {
        const next = { ...prev, [field]: value };
        // Validate immediately for already-touched fields (UXR-501: <200ms feedback)
        setTouched((t) => {
          if (!t[field]) return t;
          const minor = isUnder18(next.dateOfBirth);
          const err = validateField(field, value, { isMinor: minor });
          setErrors((e) => {
            if (err) return { ...e, [field]: err };
            const { [field]: _removed, ...rest } = e;
            return rest;
          });
          return t;
        });
        return next;
      });
    },
    [],
  );

  // ── Blur handler ─────────────────────────────────────────────────────────────

  const handleBlur = useCallback((field: keyof ManualIntakeFormValues) => {
    setTouched((prev) => ({ ...prev, [field]: true }));
    setValues((current) => {
      const minor = isUnder18(current.dateOfBirth);
      const err = validateField(field, current[field], { isMinor: minor });
      setErrors((e) => {
        if (err) return { ...e, [field]: err };
        const { [field]: _removed, ...rest } = e;
        return rest;
      });
      return current;
    });
  }, []);

  // ── Internal autosave ────────────────────────────────────────────────────────
  // Removed: replaced by shared useIntakeAutosave hook (US_030)

  // ── Manual save draft (mode-switch flush) ────────────────────────────────────

  const handleSaveDraft = useCallback(async () => {
    await flush();
  }, [flush]);

  // ── Submit ────────────────────────────────────────────────────────────────────

  const handleSubmit = useCallback(async () => {
    if (submittingRef.current) return; // EC-2 dedup

    // Mark all fields as touched to surface all errors
    const allTouched = Object.fromEntries(
      Object.keys(EMPTY_FORM).map((k) => [k, true]),
    ) as Partial<Record<keyof ManualIntakeFormValues, boolean>>;
    setTouched(allTouched);

    const validationErrors = validateAll(values, isMinor);
    if (Object.keys(validationErrors).length > 0) {
      setErrors(validationErrors);
      return;
    }

    submittingRef.current = true;
    setStatus('submitting');
    setErrorMessage(null);

    try {
      await apiPost<SubmitResponse>('/api/intake/manual/submit', { fields: values });
      setStatus('submitted');
    } catch (err) {
      setStatus('ready');
      setErrorMessage(
        err instanceof ApiError && err.status >= 400 && err.status < 500
          ? 'Please review the form and correct any errors before submitting.'
          : 'Submission failed. Please try again.',
      );
    } finally {
      submittingRef.current = false;
    }
  }, [values, isMinor]);

  return {
    values,
    errors,
    touched,
    prefilledKeys,
    status,
    lastSavedAt,
    autosaveStatus,
    errorMessage,
    isMinor,
    isGuardianAlsoMinor,
    isGuardianConsentRequired,
    insurancePrecheckStatus,
    insurancePrecheckMessage,
    handleChange,
    handleBlur,
    handleSubmit,
    handleSaveDraft,
  };
}
