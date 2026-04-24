/**
 * CodingAiUnavailableBanner — Full-width alert for AI coding service unavailability
 * on the Medical Coding Review screen (SCR-014, UXR-605).
 *
 * Separate from the extraction AiUnavailableBanner (@/features/clinical/AiUnavailableBanner)
 * which targets the clinical extraction pipeline.  This variant is scoped to the
 * coding service (circuit-breaker open or HTTP 503 from /api/coding/icd10/generate).
 *
 * Shown when:
 *   - isAiUnavailable is true (passed by parent based on ApiError status or 503)
 *
 * Action: "Switch to Manual" triggers manual coding workflow callback (UXR-605).
 *
 * ARIA: role="alert" + aria-live="assertive" for immediate screen-reader announcement.
 */

import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import EditNoteIcon from '@mui/icons-material/EditNote';

// ─── Props ────────────────────────────────────────────────────────────────────

interface CodingAiUnavailableBannerProps {
  isAiUnavailable: boolean;
  onSwitchToManual: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CodingAiUnavailableBanner({
  isAiUnavailable,
  onSwitchToManual,
}: CodingAiUnavailableBannerProps) {
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
          aria-label="Switch to manual coding — AI coding service is currently unavailable"
          sx={{ minHeight: 36 }}
        >
          Switch to Manual
        </Button>
      }
    >
      <Typography variant="body2" fontWeight={600} component="span">
        AI coding service unavailable — switch to manual coding
      </Typography>
      <Typography variant="body2" component="span" sx={{ ml: 1 }}>
        Code suggestions are temporarily unavailable. Manual review queues are still accessible.
      </Typography>
    </Alert>
  );
}
