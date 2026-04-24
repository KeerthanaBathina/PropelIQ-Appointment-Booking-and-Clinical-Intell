/**
 * AiUnavailableBanner — AI service unavailability alert for the Medical Coding screen
 * (US_048, SCR-014, UXR-605).
 *
 * Shown when the CPT coding AI service is unavailable (circuit breaker open / HTTP 503).
 * Banner text matches wireframe: "AI Unavailable: Code suggestions from the AI engine
 * are temporarily unavailable. Manual coding is available."
 *
 * Includes a dismiss action so staff can close the banner after acknowledging.
 *
 * NOTE: Distinct from `@/features/clinical/AiUnavailableBanner` (extraction pipeline)
 * and `@/components/coding/CodingAiUnavailableBanner` (ICD-10 coding pipeline).
 *
 * ARIA: role="alert" + aria-live="assertive" announces immediately to screen readers.
 */

import Alert from '@mui/material/Alert';
import Typography from '@mui/material/Typography';

// ─── Props ────────────────────────────────────────────────────────────────────

interface AiUnavailableBannerProps {
  isVisible: boolean;
  onDismiss: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AiUnavailableBanner({
  isVisible,
  onDismiss,
}: AiUnavailableBannerProps) {
  if (!isVisible) return null;

  return (
    <Alert
      severity="info"
      role="alert"
      aria-live="assertive"
      onClose={onDismiss}
      sx={{ mb: 2 }}
    >
      <Typography component="span" variant="body2" fontWeight={600}>
        AI Unavailable:
      </Typography>{' '}
      <Typography component="span" variant="body2">
        Code suggestions from the AI engine are temporarily unavailable. Manual coding
        is available. Queued documents will be processed when the service resumes.
      </Typography>
    </Alert>
  );
}
