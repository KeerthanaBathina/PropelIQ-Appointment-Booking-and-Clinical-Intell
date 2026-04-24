/**
 * UncodableAlert — MUI Alert for diagnoses that could not be mapped to an
 * ICD-10 code by the AI (US_047 edge case, AC-1).
 *
 * Shown when codes[] contains entries with codeValue = "UNCODABLE".
 * Includes a "Flag for Manual Coding" action per the task spec.
 *
 * ARIA: role="alert" announces to screen readers immediately (WCAG SC 4.1.3).
 * Design tokens: severity="warning" — warning.main (#ED6C02) background per
 * designsystem.md § Semantic Colors.
 */

import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Typography from '@mui/material/Typography';

// ─── Props ────────────────────────────────────────────────────────────────────

interface UncodableAlertProps {
  /** Count of diagnoses that returned UNCODABLE. */
  uncodableCount: number;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function UncodableAlert({ uncodableCount }: UncodableAlertProps) {
  if (uncodableCount <= 0) return null;

  return (
    <Alert
      severity="warning"
      role="alert"
      aria-live="polite"
      sx={{ mb: 2 }}
    >
      <AlertTitle>
        {uncodableCount === 1
          ? '1 diagnosis could not be mapped'
          : `${uncodableCount} diagnoses could not be mapped`}
      </AlertTitle>
      <Typography variant="body2">
        {uncodableCount === 1
          ? 'No matching ICD-10 code was found — this diagnosis has been flagged for manual coding.'
          : 'No matching ICD-10 codes were found for these diagnoses — they have been flagged for manual coding.'}
      </Typography>
    </Alert>
  );
}
