/**
 * PayerConflictDialog — MUI Dialog for resolving payer rule vs. clinical documentation conflicts.
 * (US_051, AC-1, AC-2, edge case: payer rule conflicts with clinical documentation)
 *
 * Displays a side-by-side comparison:
 *   Left  — Clinical Code: code value + AI justification
 *   Right — Payer-Preferred Code: rule description + corrective action suggestion
 *
 * Actions:
 *   "Use Clinical Code"       — Staff overrides payer rule using clinical evidence
 *   "Use Payer-Preferred Code" — Staff defers to payer rule to avoid denial
 *   "Flag for Manual Review"   — Escalates for human specialist review
 *
 * Decision is POSTed to /api/coding/resolve-conflict with:
 *   decision, clinical_code, payer_preferred_code, justification, rule_id
 *
 * WCAG 2.1 AA:
 *   role="dialog", aria-modal="true", focus trap (MUI Dialog default), keyboard close via Esc.
 *   Justification textarea required before submitting (min 10 chars).
 */

import { useState } from 'react';
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
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import AssignmentOutlinedIcon from '@mui/icons-material/AssignmentOutlined';
import GavelIcon from '@mui/icons-material/Gavel';
import LocalHospitalOutlinedIcon from '@mui/icons-material/LocalHospitalOutlined';
import type { PayerValidationResultDto } from '@/hooks/usePayerValidation';
import { resolveConflict } from '@/hooks/usePayerValidation';

// ─── Props ────────────────────────────────────────────────────────────────────

interface PayerConflictDialogProps {
  open:               boolean;
  result:             PayerValidationResultDto | null;
  /** Clinical code value currently assigned (from affected_codes[0]). */
  clinicalCode:       string;
  /** AI-generated justification for the clinical code. */
  clinicalJustification?: string;
  patientId:          string;
  onClose:            () => void;
  onResolved:         () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

type Decision = 'UseClinicalCode' | 'UsePayerPreferredCode' | 'FlagForManualReview';

export default function PayerConflictDialog({
  open,
  result,
  clinicalCode,
  clinicalJustification,
  patientId,
  onClose,
  onResolved,
}: PayerConflictDialogProps) {
  const [justification, setJustification] = useState('');
  const [submitting,    setSubmitting]    = useState(false);
  const [submitError,   setSubmitError]   = useState<string | null>(null);

  // Payer-preferred code: first suggested code from corrective actions, if any
  const payerPreferredCode = result?.corrective_actions.find(
    a => a.action_type === 'AlternativeCode',
  )?.suggested_code ?? undefined;

  const justificationValid = justification.trim().length >= 10;

  async function handleDecision(decision: Decision) {
    if (!result) return;
    if (!justificationValid) return;

    setSubmitting(true);
    setSubmitError(null);

    try {
      await resolveConflict({
        patient_id:            patientId,
        rule_id:               result.rule_id,
        decision,
        clinical_code:         clinicalCode,
        payer_preferred_code:  payerPreferredCode,
        justification:         justification.trim(),
      });

      setJustification('');
      onResolved();
      onClose();
    } catch {
      setSubmitError('Failed to save your decision. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }

  function handleClose() {
    if (submitting) return;
    setJustification('');
    setSubmitError(null);
    onClose();
  }

  if (!result) return null;

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      maxWidth="md"
      fullWidth
      aria-modal="true"
      aria-labelledby="conflict-dialog-title"
    >
      <DialogTitle id="conflict-dialog-title">
        Payer Rule Conflict — Resolution Required
        <Typography variant="caption" color="text.secondary" display="block">
          Rule {result.rule_id} · {result.description}
        </Typography>
      </DialogTitle>

      <DialogContent dividers>
        {submitError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {submitError}
          </Alert>
        )}

        {/* Side-by-side comparison */}
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
            gap: 2,
            mb: 3,
          }}
        >
          {/* Clinical Code */}
          <Box
            sx={{
              p: 2,
              borderRadius: 2,
              border: '2px solid',
              borderColor: 'primary.main',
              bgcolor: 'primary.50',
            }}
          >
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
              <LocalHospitalOutlinedIcon color="primary" fontSize="small" />
              <Typography variant="subtitle2" color="primary.main" fontWeight={700}>
                Clinical Code
              </Typography>
            </Box>
            <Chip
              label={clinicalCode}
              size="small"
              color="primary"
              sx={{ fontFamily: 'monospace', fontWeight: 700, mb: 1 }}
            />
            {clinicalJustification && (
              <Typography variant="caption" display="block" color="text.secondary">
                {clinicalJustification}
              </Typography>
            )}
          </Box>

          {/* Payer-Preferred Code */}
          <Box
            sx={{
              p: 2,
              borderRadius: 2,
              border: '2px solid',
              borderColor: result.severity === 'error' ? 'error.main' : 'warning.main',
              bgcolor: result.severity === 'error' ? 'error.50' : 'warning.50',
            }}
          >
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
              <GavelIcon
                color={result.severity === 'error' ? 'error' : 'warning'}
                fontSize="small"
              />
              <Typography
                variant="subtitle2"
                color={result.severity === 'error' ? 'error.main' : 'warning.main'}
                fontWeight={700}
              >
                Payer Rule
              </Typography>
            </Box>
            <Typography variant="body2" sx={{ mb: 0.5 }}>
              {result.description}
            </Typography>
            {payerPreferredCode && (
              <Chip
                label={payerPreferredCode}
                size="small"
                color={result.severity === 'error' ? 'error' : 'warning'}
                sx={{ fontFamily: 'monospace', fontWeight: 700 }}
                aria-label={`Payer-preferred code: ${payerPreferredCode}`}
              />
            )}
          </Box>
        </Box>

        <Divider sx={{ mb: 2 }} />

        {/* Justification */}
        <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, mb: 1 }}>
          <AssignmentOutlinedIcon fontSize="small" sx={{ mt: 0.25, color: 'text.secondary' }} />
          <Typography variant="subtitle2" gutterBottom>
            Clinical Justification <span style={{ color: 'red' }}>*</span>
          </Typography>
        </Box>
        <TextField
          multiline
          rows={3}
          fullWidth
          placeholder="Provide clinical rationale for your decision (minimum 10 characters)…"
          value={justification}
          onChange={e => setJustification(e.target.value)}
          error={justification.length > 0 && !justificationValid}
          helperText={
            justification.length > 0 && !justificationValid
              ? `${10 - justification.trim().length} more character(s) required`
              : 'Decision is logged in the coding audit trail per HIPAA requirements.'
          }
          inputProps={{ 'aria-label': 'Clinical justification for conflict resolution' }}
          disabled={submitting}
        />
      </DialogContent>

      <DialogActions sx={{ flexWrap: 'wrap', gap: 1, p: 2 }}>
        <Button
          onClick={handleClose}
          disabled={submitting}
          color="inherit"
        >
          Cancel
        </Button>

        <Box sx={{ flex: 1 }} />

        <Button
          variant="outlined"
          color="warning"
          disabled={!justificationValid || submitting}
          onClick={() => handleDecision('FlagForManualReview')}
          startIcon={submitting ? <CircularProgress size={14} /> : undefined}
          aria-label="Flag encounter for manual payer rule verification"
        >
          Flag for Manual Review
        </Button>

        {payerPreferredCode && (
          <Button
            variant="outlined"
            color={result.severity === 'error' ? 'error' : 'warning'}
            disabled={!justificationValid || submitting}
            onClick={() => handleDecision('UsePayerPreferredCode')}
            aria-label={`Use payer-preferred code ${payerPreferredCode}`}
          >
            Use Payer Code ({payerPreferredCode})
          </Button>
        )}

        <Button
          variant="contained"
          color="primary"
          disabled={!justificationValid || submitting}
          onClick={() => handleDecision('UseClinicalCode')}
          aria-label={`Keep clinical code ${clinicalCode}`}
        >
          Use Clinical Code ({clinicalCode})
        </Button>
      </DialogActions>
    </Dialog>
  );
}
