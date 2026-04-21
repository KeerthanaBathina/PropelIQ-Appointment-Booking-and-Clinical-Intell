/**
 * ManualIntakeForm — sectioned intake form (US_028 AC-1, AC-2, AC-3, SCR-009).
 *
 * Sections matching wireframe-SCR-009-manual-intake.html:
 *   1. Personal Information  — First/Last, DOB, Gender, Phone, Emergency Contact
 *   2. Medical History       — Allergies*, Medications, Pre-existing Conditions
 *   3. Insurance Information — Provider, Policy Number
 *   4. Consent               — Checkbox confirmation
 *
 * Layout:
 *   - Single column on mobile (<sm), two-column grid on desktop (sm+) (UXR-303).
 *   - Required fields marked with * in label.
 *   - Pre-filled fields show <PrefilledFieldIndicator> below the input (AC-2).
 *   - Validation errors displayed inline via TextField helperText (UXR-501 <200ms).
 *
 * Accessibility (WCAG 2.1 AA):
 *   - Every input has an associated <label> via TextField's `label` prop.
 *   - Error fields use `aria-invalid="true"` and `aria-describedby` linked to error text.
 *   - `role="group"` + `aria-labelledby` on each section heading.
 */

import Box from '@mui/material/Box';
import Checkbox from '@mui/material/Checkbox';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormHelperText from '@mui/material/FormHelperText';
import Grid from '@mui/material/Grid';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import InsurancePrecheckStatusBadge, { type InsurancePrecheckStatus } from '@/components/intake/InsurancePrecheckStatusBadge';
import PrefilledFieldIndicator from '@/components/intake/PrefilledFieldIndicator';
import type {
  ManualIntakeFormValues,
  FormErrors,
} from '@/hooks/useManualIntakeForm';

interface ManualIntakeFormProps {
  values: ManualIntakeFormValues;
  errors: FormErrors;
  touched: Partial<Record<keyof ManualIntakeFormValues, boolean>>;
  prefilledKeys: Set<keyof ManualIntakeFormValues>;
  disabled?: boolean;
  /** True when patient DOB indicates age under 18 — shows Guardian Consent section (US_031 AC-1). */
  isMinor?: boolean;
  /** True when the guardian entered is also under 18 — shows EC-1 blocker alert. */
  isGuardianAlsoMinor?: boolean;
  /** Current insurance soft pre-check status (US_031 AC-2, AC-4, EC-2). */
  insurancePrecheckStatus?: InsurancePrecheckStatus;
  /** Optional server-provided explanation for 'needs-review' status (AC-4). */
  insurancePrecheckMessage?: string | null;
  onChange: (field: keyof ManualIntakeFormValues, value: string | boolean) => void;
  onBlur: (field: keyof ManualIntakeFormValues) => void;
}

const GENDER_OPTIONS = [
  'Female',
  'Male',
  'Non-binary',
  'Prefer not to say',
] as const;

// ─── Helper: show error only when field is touched ─────────────────────────────

function fieldError(
  field: keyof ManualIntakeFormValues,
  errors: FormErrors,
  touched: Partial<Record<keyof ManualIntakeFormValues, boolean>>,
): string | undefined {
  return touched[field] ? errors[field] : undefined;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ManualIntakeForm({
  values,
  errors,
  touched,
  prefilledKeys,
  disabled = false,
  isMinor = false,
  isGuardianAlsoMinor = false,
  insurancePrecheckStatus = 'idle',
  insurancePrecheckMessage = null,
  onChange,
  onBlur,
}: ManualIntakeFormProps) {
  const isPrefilled = (key: keyof ManualIntakeFormValues) => prefilledKeys.has(key);

  return (
    <Box component="div">
      {/* ── 1. Personal Information ──────────────────────────────────────── */}
      <Box
        role="group"
        aria-labelledby="section-personal-heading"
        sx={{ mb: 4 }}
      >
        <Typography
          id="section-personal-heading"
          variant="h6"
          sx={{ mb: 2, fontWeight: 500 }}
        >
          Personal Information
        </Typography>

        <Grid container spacing={2}>
          {/* First Name / Last Name */}
          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-fname"
              label="First Name *"
              fullWidth
              value={values.firstName}
              error={!!fieldError('firstName', errors, touched)}
              helperText={fieldError('firstName', errors, touched)}
              disabled={disabled}
              inputProps={{ 'aria-required': 'true' }}
              onChange={(e) => onChange('firstName', e.target.value)}
              onBlur={() => onBlur('firstName')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('firstName')} />
          </Grid>

          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-lname"
              label="Last Name *"
              fullWidth
              value={values.lastName}
              error={!!fieldError('lastName', errors, touched)}
              helperText={fieldError('lastName', errors, touched)}
              disabled={disabled}
              inputProps={{ 'aria-required': 'true' }}
              onChange={(e) => onChange('lastName', e.target.value)}
              onBlur={() => onBlur('lastName')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('lastName')} />
          </Grid>

          {/* DOB / Gender */}
          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-dob"
              label="Date of Birth *"
              type="date"
              fullWidth
              value={values.dateOfBirth}
              error={!!fieldError('dateOfBirth', errors, touched)}
              helperText={fieldError('dateOfBirth', errors, touched) ?? 'MM/DD/YYYY'}
              disabled={disabled}
              InputLabelProps={{ shrink: true }}
              inputProps={{ 'aria-required': 'true' }}
              onChange={(e) => onChange('dateOfBirth', e.target.value)}
              onBlur={() => onBlur('dateOfBirth')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('dateOfBirth')} />
          </Grid>

          <Grid item xs={12} sm={6}>
            <FormControl
              fullWidth
              error={!!fieldError('gender', errors, touched)}
              disabled={disabled}
            >
              <InputLabel id="mi-gender-label">Gender *</InputLabel>
              <Select
                labelId="mi-gender-label"
                id="mi-gender"
                value={values.gender}
                label="Gender *"
                inputProps={{ 'aria-required': 'true' }}
                onChange={(e) => onChange('gender', e.target.value)}
                onBlur={() => onBlur('gender')}
              >
                {GENDER_OPTIONS.map((opt) => (
                  <MenuItem key={opt} value={opt}>
                    {opt}
                  </MenuItem>
                ))}
              </Select>
              {fieldError('gender', errors, touched) && (
                <FormHelperText>{fieldError('gender', errors, touched)}</FormHelperText>
              )}
            </FormControl>
            <PrefilledFieldIndicator visible={isPrefilled('gender')} />
          </Grid>

          {/* Phone / Emergency Contact */}
          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-phone"
              label="Phone *"
              type="tel"
              fullWidth
              value={values.phone}
              error={!!fieldError('phone', errors, touched)}
              helperText={fieldError('phone', errors, touched) ?? 'e.g., 555-123-4567'}
              disabled={disabled}
              inputProps={{ 'aria-required': 'true' }}
              onChange={(e) => onChange('phone', e.target.value)}
              onBlur={() => onBlur('phone')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('phone')} />
          </Grid>

          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-emergency"
              label="Emergency Contact"
              fullWidth
              value={values.emergencyContact}
              error={!!fieldError('emergencyContact', errors, touched)}
              helperText={
                fieldError('emergencyContact', errors, touched) ??
                'Name and phone number'
              }
              disabled={disabled}
              onChange={(e) => onChange('emergencyContact', e.target.value)}
              onBlur={() => onBlur('emergencyContact')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('emergencyContact')} />
          </Grid>
        </Grid>
      </Box>

      <Divider sx={{ my: 3 }} />

      {/* ── 2. Medical History ───────────────────────────────────────────── */}
      <Box
        role="group"
        aria-labelledby="section-medical-heading"
        sx={{ mb: 4 }}
      >
        <Typography
          id="section-medical-heading"
          variant="h6"
          sx={{ mb: 2, fontWeight: 500 }}
        >
          Medical History
        </Typography>

        <Grid container spacing={2}>
          <Grid item xs={12}>
            <TextField
              id="mi-allergies"
              label="Known Allergies *"
              multiline
              minRows={3}
              fullWidth
              value={values.knownAllergies}
              error={!!fieldError('knownAllergies', errors, touched)}
              helperText={fieldError('knownAllergies', errors, touched)}
              disabled={disabled}
              inputProps={{ 'aria-required': 'true' }}
              onChange={(e) => onChange('knownAllergies', e.target.value)}
              onBlur={() => onBlur('knownAllergies')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('knownAllergies')} />
          </Grid>

          <Grid item xs={12}>
            <TextField
              id="mi-medications"
              label="Current Medications"
              multiline
              minRows={3}
              fullWidth
              placeholder="List medications with dosages"
              value={values.currentMedications}
              error={!!fieldError('currentMedications', errors, touched)}
              helperText={fieldError('currentMedications', errors, touched)}
              disabled={disabled}
              onChange={(e) => onChange('currentMedications', e.target.value)}
              onBlur={() => onBlur('currentMedications')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('currentMedications')} />
          </Grid>

          <Grid item xs={12}>
            <TextField
              id="mi-conditions"
              label="Pre-existing Conditions"
              multiline
              minRows={3}
              fullWidth
              placeholder="List any chronic conditions or past surgeries"
              value={values.preExistingConditions}
              error={!!fieldError('preExistingConditions', errors, touched)}
              helperText={fieldError('preExistingConditions', errors, touched)}
              disabled={disabled}
              onChange={(e) => onChange('preExistingConditions', e.target.value)}
              onBlur={() => onBlur('preExistingConditions')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('preExistingConditions')} />
          </Grid>
        </Grid>
      </Box>

      <Divider sx={{ my: 3 }} />

      {/* ── 3. Insurance Information ────────────────────────────────────── */}
      <Box
        role="group"
        aria-labelledby="section-insurance-heading"
        sx={{ mb: 4 }}
      >
        <Typography
          id="section-insurance-heading"
          variant="h6"
          sx={{ mb: 2, fontWeight: 500 }}
        >
          Insurance Information
        </Typography>

        <Grid container spacing={2}>
          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-insurance"
              label="Insurance Provider"
              fullWidth
              placeholder="e.g., Blue Cross Blue Shield"
              value={values.insuranceProvider}
              error={!!fieldError('insuranceProvider', errors, touched)}
              helperText={fieldError('insuranceProvider', errors, touched)}
              disabled={disabled}
              onChange={(e) => onChange('insuranceProvider', e.target.value)}
              onBlur={() => onBlur('insuranceProvider')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('insuranceProvider')} />
          </Grid>

          <Grid item xs={12} sm={6}>
            <TextField
              id="mi-policy"
              label="Policy Number"
              fullWidth
              placeholder="e.g., BCB-123456789"
              value={values.policyNumber}
              error={!!fieldError('policyNumber', errors, touched)}
              helperText={fieldError('policyNumber', errors, touched)}
              disabled={disabled}
              onChange={(e) => onChange('policyNumber', e.target.value)}
              onBlur={() => onBlur('policyNumber')}
            />
            <PrefilledFieldIndicator visible={isPrefilled('policyNumber')} />
          </Grid>

          {/* Insurance soft pre-check result (US_031 AC-2, AC-4, EC-2) */}
          <Grid item xs={12}>
            <InsurancePrecheckStatusBadge
              status={insurancePrecheckStatus}
              message={insurancePrecheckMessage}
            />
          </Grid>
        </Grid>
      </Box>

      <Divider sx={{ my: 3 }} />

      {/* ── 3b. Guardian Consent (US_031 AC-1) — shown only when patient is under 18 ── */}
      {isMinor && (
        <Box
          role="group"
          aria-labelledby="section-guardian-heading"
          sx={{ mb: 4 }}
        >
          <Typography
            id="section-guardian-heading"
            variant="h6"
            sx={{ mb: 1, fontWeight: 500 }}
          >
            Guardian Consent
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            This patient is under 18. A parent or legal guardian must provide consent.
          </Typography>

          {/* EC-1: Guardian is also under 18 — block and explain */}
          {isGuardianAlsoMinor && (
            <Alert severity="error" sx={{ mb: 2 }} role="alert">
              The guardian you entered appears to be under 18. Guardian age must be 18 or older.
              A minor cannot provide consent for another minor.
            </Alert>
          )}

          <Grid container spacing={2}>
            <Grid item xs={12} sm={6}>
              <TextField
                id="mi-guardian-name"
                label="Guardian Full Name *"
                fullWidth
                value={values.guardianName}
                error={!!fieldError('guardianName', errors, touched)}
                helperText={fieldError('guardianName', errors, touched)}
                disabled={disabled}
                inputProps={{ 'aria-required': 'true' }}
                onChange={(e) => onChange('guardianName', e.target.value)}
                onBlur={() => onBlur('guardianName')}
              />
            </Grid>

            <Grid item xs={12} sm={6}>
              <TextField
                id="mi-guardian-relationship"
                label="Relationship to Patient *"
                fullWidth
                placeholder="e.g., Parent, Legal Guardian"
                value={values.guardianRelationship}
                error={!!fieldError('guardianRelationship', errors, touched)}
                helperText={fieldError('guardianRelationship', errors, touched)}
                disabled={disabled}
                inputProps={{ 'aria-required': 'true' }}
                onChange={(e) => onChange('guardianRelationship', e.target.value)}
                onBlur={() => onBlur('guardianRelationship')}
              />
            </Grid>

            <Grid item xs={12} sm={6}>
              <TextField
                id="mi-guardian-dob"
                label="Guardian Date of Birth *"
                type="date"
                fullWidth
                value={values.guardianDateOfBirth}
                error={!!fieldError('guardianDateOfBirth', errors, touched)}
                helperText={
                  fieldError('guardianDateOfBirth', errors, touched) ??
                  'Must be 18 or older'
                }
                disabled={disabled}
                InputLabelProps={{ shrink: true }}
                inputProps={{ 'aria-required': 'true' }}
                onChange={(e) => onChange('guardianDateOfBirth', e.target.value)}
                onBlur={() => onBlur('guardianDateOfBirth')}
              />
            </Grid>

            <Grid item xs={12}>
              <FormControl
                error={!!fieldError('guardianConsentAcknowledged', errors, touched)}
                component="fieldset"
              >
                <FormControlLabel
                  control={
                    <Checkbox
                      id="mi-guardian-consent"
                      checked={values.guardianConsentAcknowledged}
                      disabled={disabled || isGuardianAlsoMinor}
                      inputProps={{ 'aria-required': 'true' }}
                      onChange={(e) => {
                        onChange('guardianConsentAcknowledged', e.target.checked);
                        onBlur('guardianConsentAcknowledged');
                      }}
                    />
                  }
                  label="I am the parent or legal guardian of this patient and consent to the collection and use of their health information for care purposes."
                />
                {fieldError('guardianConsentAcknowledged', errors, touched) && (
                  <FormHelperText>
                    {fieldError('guardianConsentAcknowledged', errors, touched)}
                  </FormHelperText>
                )}
              </FormControl>
            </Grid>
          </Grid>
        </Box>
      )}

      {isMinor && <Divider sx={{ my: 3 }} />}

      {/* ── 4. Consent ──────────────────────────────────────────────────── */}
      <Box
        role="group"
        aria-labelledby="section-consent-heading"
        sx={{ mb: 2 }}
      >
        <FormControl
          error={!!fieldError('consentGiven', errors, touched)}
          component="fieldset"
        >
          <FormControlLabel
            control={
              <Checkbox
                id="mi-consent"
                checked={values.consentGiven}
                disabled={disabled}
                inputProps={{ 'aria-required': 'true' }}
                onChange={(e) => {
                  onChange('consentGiven', e.target.checked);
                  onBlur('consentGiven');
                }}
              />
            }
            label="I confirm this information is accurate and consent to its use for my care."
          />
          {fieldError('consentGiven', errors, touched) && (
            <FormHelperText>
              {fieldError('consentGiven', errors, touched)}
            </FormHelperText>
          )}
        </FormControl>
      </Box>
    </Box>
  );
}
