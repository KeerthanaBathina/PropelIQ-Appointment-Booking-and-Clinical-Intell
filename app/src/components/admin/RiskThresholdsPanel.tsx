/**
 * RiskThresholdsPanel — AI risk-score configuration for SCR-015 Config tab (US_058 AC-4, US_060 AC-3, AC-4).
 *
 * Fields:
 *   - highRiskThreshold:        MUI Slider 0-100, step 5
 *   - mediumRiskThreshold:      MUI Slider 0-100, step 5
 *   - minAppointmentsForAiScore: TextField (number)
 *   - autoOutreach:             Switch
 *   - Scoring parameter weights (priorNoShowsWeight, cancellationHistoryWeight,
 *     appointmentLeadTimeWeight) — must sum to 1.0
 *
 * UXR-501: Inline validation on onChange, <200 ms feedback.
 * Constraint: mediumRiskThreshold < highRiskThreshold.
 * UXR-502: Skeleton loading.
 * US_060 AC-3: RiskScorePreview inline component.
 * US_060 AC-4: Info banner + scoring weights with sum-to-1.0 validation.
 * UXR-102: Confirmation dialog before save.
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import FormControlLabel from '@mui/material/FormControlLabel';
import Skeleton from '@mui/material/Skeleton';
import Slider from '@mui/material/Slider';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';
import { useRiskThresholds, useUpdateRiskThresholds } from '@/hooks/useAdminConfig';
import { useToast } from '@/components/common/ToastProvider';
import type { RiskThresholdsConfig } from '@/types/adminConfig';

// ─── Scoring weights ──────────────────────────────────────────────────────────

interface ScoringWeights {
  priorNoShowsWeight:         number; // 0.00–1.00
  cancellationHistoryWeight:  number;
  appointmentLeadTimeWeight:  number;
}

const DEFAULT_WEIGHTS: ScoringWeights = {
  priorNoShowsWeight:        0.50,
  cancellationHistoryWeight: 0.30,
  appointmentLeadTimeWeight: 0.20,
};

// ─── Validation ───────────────────────────────────────────────────────────────

interface FormErrors {
  highRiskThreshold?:         string;
  mediumRiskThreshold?:       string;
  minAppointmentsForAiScore?: string;
  weights?:                   string;
}

function validate(f: RiskThresholdsConfig, w: ScoringWeights): FormErrors {
  const errors: FormErrors = {};
  const high   = Number(f.highRiskThreshold);
  const medium = Number(f.mediumRiskThreshold);
  const min    = Number(f.minAppointmentsForAiScore);

  if (isNaN(high) || high < 0 || high > 100) {
    errors.highRiskThreshold = 'Must be 0–100';
  }
  if (isNaN(medium) || medium < 0 || medium > 100) {
    errors.mediumRiskThreshold = 'Must be 0–100';
  }
  if (!errors.highRiskThreshold && !errors.mediumRiskThreshold && medium >= high) {
    errors.mediumRiskThreshold = 'Must be less than high-risk threshold';
  }
  if (isNaN(min) || min < 1) {
    errors.minAppointmentsForAiScore = 'Must be ≥ 1';
  }

  const sum = +(w.priorNoShowsWeight + w.cancellationHistoryWeight + w.appointmentLeadTimeWeight).toFixed(2);
  if (sum !== 1.0) {
    errors.weights = `Weights must sum to 1.0 (currently ${sum.toFixed(2)})`;
  }
  return errors;
}

// ─── Risk Score Preview ───────────────────────────────────────────────────────

const SAMPLE_PATIENTS = [
  { name: 'John A.',  score: 82 },
  { name: 'Maria B.', score: 61 },
  { name: 'Sam C.',   score: 34 },
];

interface RiskScorePreviewProps {
  high:   number;
  medium: number;
}

function RiskScorePreview({ high, medium }: RiskScorePreviewProps) {
  function bucket(score: number): { label: string; color: 'error' | 'warning' | 'success' } {
    if (score >= high)   return { label: 'High Risk',   color: 'error' };
    if (score >= medium) return { label: 'Medium Risk', color: 'warning' };
    return                      { label: 'Low Risk',    color: 'success' };
  }

  return (
    <Box>
      <Alert severity="info" sx={{ mb: 2 }}>
        Risk score changes apply to future appointments. Existing appointments recalculate in the next batch run.
      </Alert>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1, fontWeight: 600 }}>
        Sample Patient Preview
      </Typography>
      {SAMPLE_PATIENTS.map(p => {
        const b = bucket(p.score);
        return (
          <Box key={p.name} sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 0.75 }}>
            <Typography variant="body2" sx={{ minWidth: 72 }}>{p.name}</Typography>
            <Typography variant="body2" fontWeight={600} sx={{ minWidth: 28 }}>{p.score}</Typography>
            <Chip label={b.label} color={b.color} size="small" />
          </Box>
        );
      })}
    </Box>
  );
}

// ─── Slider row ───────────────────────────────────────────────────────────────

interface ThresholdSliderProps {
  label:    string;
  value:    number;
  onChange: (v: number) => void;
  error?:   string;
  helperText: string;
  ariaLabel?: string;
}

function ThresholdSlider({ label, value, onChange, error, helperText, ariaLabel }: ThresholdSliderProps) {
  const MARKS = [0, 25, 50, 75, 100].map(v => ({ value: v, label: `${v}` }));
  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: 0.5 }}>
        <Typography variant="body2">{label}</Typography>
        <Typography variant="body2" fontWeight={600} color={error ? 'error.main' : 'primary.main'}>
          {value}%
        </Typography>
      </Box>
      <Slider
        value={value}
        min={0}
        max={100}
        step={5}
        marks={MARKS}
        onChange={(_, v) => onChange(v as number)}
        aria-label={ariaLabel ?? label}
        color={error ? 'error' : 'primary'}
        sx={{ mt: 0.5 }}
      />
      <Typography variant="caption" color={error ? 'error.main' : 'text.secondary'}>
        {error ?? helperText}
      </Typography>
    </Box>
  );
}

// ─── Main Component ───────────────────────────────────────────────────────────

export default function RiskThresholdsPanel() {
  const { data, isLoading } = useRiskThresholds();
  const { mutate: save, isLoading: isSaving } = useUpdateRiskThresholds();
  const { showToast } = useToast();

  const [form, setForm] = useState<RiskThresholdsConfig>({
    highRiskThreshold:         75,
    mediumRiskThreshold:       45,
    minAppointmentsForAiScore: 3,
    autoOutreach:              true,
  });
  const [weights, setWeights] = useState<ScoringWeights>(DEFAULT_WEIGHTS);
  const [errors,  setErrors]  = useState<FormErrors>({});
  const [dirty,   setDirty]   = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  useEffect(() => {
    if (data) {
      setForm(data);
      setDirty(false);
    }
  }, [data]);

  // UXR-501: validate on every change
  function handleChange(field: keyof RiskThresholdsConfig, value: number | boolean) {
    const next = { ...form, [field]: value };
    setForm(next);
    setErrors(validate(next, weights));
    setDirty(true);
  }

  function handleWeightChange(field: keyof ScoringWeights, rawValue: string) {
    const parsed = parseFloat(rawValue);
    const value  = isNaN(parsed) ? 0 : Math.min(1, Math.max(0, parsed));
    const next   = { ...weights, [field]: value };
    setWeights(next);
    setErrors(validate(form, next));
    setDirty(true);
  }

  function handleSaveClick() {
    const errs = validate(form, weights);
    if (Object.keys(errs).length > 0) {
      setErrors(errs);
      return;
    }
    setConfirmOpen(true);
  }

  function handleConfirmedSave() {
    save(form, {
      onSuccess: () => {
        showToast({ message: 'Risk thresholds saved.', severity: 'success' });
        setDirty(false);
        setConfirmOpen(false);
      },
      onError: () => {
        showToast({ message: 'Failed to save. Please retry.', severity: 'error' });
        setConfirmOpen(false);
      },
    });
  }

  if (isLoading) {
    return (
      <Box sx={{ maxWidth: 500 }}>
        {[0, 1, 2, 3, 4].map(i => (
          <Skeleton key={i} variant="rectangular" height={40} sx={{ mb: 2 }} />
        ))}
      </Box>
    );
  }

  const hasErrors = Object.keys(errors).length > 0;
  const weightSum = +(weights.priorNoShowsWeight + weights.cancellationHistoryWeight + weights.appointmentLeadTimeWeight).toFixed(2);

  return (
    <Box sx={{ maxWidth: 520 }}>
      <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 3 }}>
        AI Risk Configuration
      </Typography>

      {/* ── Threshold sliders ── */}
      <Typography variant="subtitle2" sx={{ mb: 2 }}>Risk Thresholds</Typography>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3, mb: 3, px: 1 }}>
        <ThresholdSlider
          label="High-Risk Threshold"
          value={form.highRiskThreshold}
          onChange={v => handleChange('highRiskThreshold', v)}
          error={errors.highRiskThreshold}
          helperText="Patients at or above this score are flagged high-risk."
          ariaLabel="High-risk threshold slider"
        />
        <ThresholdSlider
          label="Medium-Risk Threshold"
          value={form.mediumRiskThreshold}
          onChange={v => handleChange('mediumRiskThreshold', v)}
          error={errors.mediumRiskThreshold}
          helperText="Patients at or above this score (below high) are flagged medium-risk."
          ariaLabel="Medium-risk threshold slider"
        />
      </Box>

      {/* ── Risk Score Preview (AC-3) ── */}
      <Box sx={{ mb: 3 }}>
        <RiskScorePreview high={form.highRiskThreshold} medium={form.mediumRiskThreshold} />
      </Box>

      <Divider sx={{ mb: 3 }} />

      {/* ── Scoring parameter weights (AC-4) ── */}
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        Scoring Parameter Weights
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Values must sum to 1.0 (currently{' '}
        <strong style={{ color: errors.weights ? 'red' : 'inherit' }}>{weightSum.toFixed(2)}</strong>).
      </Typography>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mb: 3 }}>
        <TextField
          label="Prior No-Shows Weight"
          type="number"
          size="small"
          inputProps={{ min: 0, max: 1, step: 0.05, 'aria-label': 'Prior no-shows weight' }}
          value={weights.priorNoShowsWeight}
          onChange={e => handleWeightChange('priorNoShowsWeight', e.target.value)}
          error={!!errors.weights}
          helperText="e.g. 0.50"
        />
        <TextField
          label="Cancellation History Weight"
          type="number"
          size="small"
          inputProps={{ min: 0, max: 1, step: 0.05, 'aria-label': 'Cancellation history weight' }}
          value={weights.cancellationHistoryWeight}
          onChange={e => handleWeightChange('cancellationHistoryWeight', e.target.value)}
          error={!!errors.weights}
          helperText="e.g. 0.30"
        />
        <TextField
          label="Appointment Lead Time Weight"
          type="number"
          size="small"
          inputProps={{ min: 0, max: 1, step: 0.05, 'aria-label': 'Appointment lead time weight' }}
          value={weights.appointmentLeadTimeWeight}
          onChange={e => handleWeightChange('appointmentLeadTimeWeight', e.target.value)}
          error={!!errors.weights}
          helperText="e.g. 0.20"
        />
        {errors.weights && (
          <Alert severity="error" role="alert">{errors.weights}</Alert>
        )}
      </Box>

      <Divider sx={{ mb: 3 }} />

      {/* ── Other settings ── */}
      <Typography variant="subtitle2" sx={{ mb: 2 }}>Other Settings</Typography>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, mb: 3 }}>
        <TextField
          label="Min Appointments for AI Score"
          type="number"
          size="small"
          inputProps={{ min: 1, 'aria-label': 'Minimum appointments for AI score' }}
          value={form.minAppointmentsForAiScore}
          onChange={e => handleChange('minAppointmentsForAiScore', Number(e.target.value))}
          error={!!errors.minAppointmentsForAiScore}
          helperText={errors.minAppointmentsForAiScore ?? 'Minimum completed appointments before an AI score is generated.'}
        />

        <FormControlLabel
          control={
            <Switch
              checked={form.autoOutreach}
              onChange={e => handleChange('autoOutreach', e.target.checked)}
              color="primary"
              inputProps={{ 'aria-label': 'Enable auto outreach for high-risk patients' }}
            />
          }
          label="Auto Outreach for High-Risk Patients"
        />
      </Box>

      <Button
        variant="contained"
        size="small"
        onClick={handleSaveClick}
        disabled={isSaving || hasErrors || !dirty}
        aria-label="Save risk configuration"
        startIcon={isSaving ? <CircularProgress size={14} color="inherit" /> : undefined}
      >
        {isSaving ? 'Saving…' : 'Save Changes'}
      </Button>

      {/* Confirmation dialog (UXR-102) */}
      <Dialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        aria-labelledby="risk-confirm-dialog-title"
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle id="risk-confirm-dialog-title">Save Risk Configuration?</DialogTitle>
        <DialogContent>
          <Typography variant="body2">
            Updating risk thresholds will affect how patients are classified. Existing appointments will recalculate in the next batch run. Future appointments use the new thresholds immediately.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmOpen(false)} aria-label="Cancel save">Cancel</Button>
          <Button
            variant="contained"
            onClick={handleConfirmedSave}
            disabled={isSaving}
            aria-label="Confirm save risk configuration"
          >
            Confirm Save
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
