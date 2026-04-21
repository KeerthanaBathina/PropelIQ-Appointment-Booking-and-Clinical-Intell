/**
 * IntakeModeSwitchBanner — shared status and messaging banner for intake mode transitions (US_029).
 *
 * Variants:
 *   "ai-unavailable"      — AI 503/502: disables switch-to-AI, shows UXR-605 fallback text with
 *                           "Switch to Manual Form" action button (EC-2).
 *   "resumed-from-manual" — Shown on SCR-008 when the patient navigated back from the manual form;
 *                           confirms progress was preserved and the AI will continue from the next
 *                           uncollected field (AC-2, AC-3).
 *   "prefilled-from-ai"   — Shown on SCR-009 when the manual form was pre-filled from an AI session;
 *                           replaces the inline success alert in ManualIntakePage (AC-2).
 *
 * Accessibility: role="alert" for ai-unavailable (assertive), role="status" for informational variants.
 */

import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';

// ─── Types ────────────────────────────────────────────────────────────────────

export type IntakeModeSwitchVariant =
  | 'ai-unavailable'       // UXR-605: AI service down — show switch-to-manual CTA
  | 'resumed-from-manual'  // AI page: patient returned from manual form
  | 'prefilled-from-ai';   // Manual page: fields pre-filled from AI intake

interface IntakeModeSwitchBannerProps {
  variant: IntakeModeSwitchVariant;
  /** Called when the user clicks "Switch to Manual Form" in the ai-unavailable variant. */
  onSwitchToManual?: () => void;
  /** Called when the user closes the banner (adds a dismiss/close icon). */
  onDismiss?: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function IntakeModeSwitchBanner({
  variant,
  onSwitchToManual,
  onDismiss,
}: IntakeModeSwitchBannerProps) {
  if (variant === 'ai-unavailable') {
    return (
      <Alert
        severity="warning"
        role="alert"
        aria-live="assertive"
        action={
          onSwitchToManual ? (
            <Button color="inherit" size="small" onClick={onSwitchToManual}>
              Switch to Manual Form
            </Button>
          ) : undefined
        }
        sx={{ mb: 2 }}
      >
        AI intake temporarily unavailable. You can continue with the manual form to complete
        your intake without losing any progress.
      </Alert>
    );
  }

  if (variant === 'resumed-from-manual') {
    return (
      <Alert
        severity="info"
        role="status"
        aria-live="polite"
        sx={{ mb: 2 }}
        onClose={onDismiss}
      >
        Resumed from manual form. Your previously entered values have been preserved and the
        AI will continue from the next uncollected field.
      </Alert>
    );
  }

  // prefilled-from-ai
  return (
    <Alert
      severity="success"
      role="status"
      aria-live="polite"
      sx={{ mb: 2 }}
      icon={false}
      onClose={onDismiss}
    >
      <strong>AI intake data pre-filled.</strong> Fields collected from your AI conversation are
      highlighted below. Please review and correct any values before submitting.
    </Alert>
  );
}
