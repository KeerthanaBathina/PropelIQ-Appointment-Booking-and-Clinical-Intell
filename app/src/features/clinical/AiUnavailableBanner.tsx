/**
 * AiUnavailableBanner — MUI Alert shown on PatientProfile360 when the AI service is
 * completely unavailable (US_046 AC-4, UXR-605).
 *
 * Displays:
 *   "AI unavailable — switch to manual" with a CTA button that activates the
 *   manual data entry form for the patient.
 *
 * Hidden when AI is available (isAiUnavailable === false).
 * Uses `role="alert"` so screen readers announce it immediately (UXR-206).
 */

import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import EditNoteIcon from '@mui/icons-material/EditNote';

// ─── Props ────────────────────────────────────────────────────────────────────

interface AiUnavailableBannerProps {
  isAiUnavailable: boolean;
  onSwitchToManual: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AiUnavailableBanner({
  isAiUnavailable,
  onSwitchToManual,
}: AiUnavailableBannerProps) {
  if (!isAiUnavailable) return null;

  return (
    <Alert
      severity="error"
      role="alert"
      aria-live="assertive"
      sx={{ mb: 2 }}
      action={
        <Button
          color="inherit"
          size="small"
          variant="outlined"
          startIcon={<EditNoteIcon />}
          onClick={onSwitchToManual}
          aria-label="Switch to manual data entry — AI service is currently unavailable"
          sx={{ minHeight: 36 }}
        >
          Switch to Manual
        </Button>
      }
    >
      <Typography variant="body2" fontWeight={600} component="span">
        AI unavailable — switch to manual
      </Typography>
      <Typography variant="body2" component="span" sx={{ ml: 1 }}>
        The AI extraction service is currently down. You can enter clinical data manually.
      </Typography>
    </Alert>
  );
}
