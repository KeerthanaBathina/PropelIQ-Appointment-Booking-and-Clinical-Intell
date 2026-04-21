/**
 * IntakeProgressHeader — SCR-008 progress bar, autosave indicator, and session status.
 *
 * Wireframe spec: progress-section strip between the chat header and the message body.
 *   - Shows "4/8 mandatory fields" text
 *   - Linear progress bar (determinate, MUI LinearProgress)
 *   - Auto-saved timestamp ("Auto-saved X ago")
 *
 * UXR-004: auto-save freshness must be displayed.
 * AC-5: progress must reflect actual fields collected / total required.
 * Accessibility: role="status" aria-live="polite" so screen readers hear updates.
 */

import Box from '@mui/material/Box';
import Fade from '@mui/material/Fade';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import { memo, useMemo } from 'react';
import type { AutosaveStatus } from '@/hooks/useIntakeAutosave';
import { AUTOSAVE_FAILED_MESSAGE } from '@/hooks/useIntakeAutosave';

// ─── Types ────────────────────────────────────────────────────────────────────

interface IntakeProgressHeaderProps {
  collectedCount: number;
  totalRequired: number;
  /** ISO timestamp of last autosave, or null when not yet saved */
  lastSavedAt: string | null;
  /**
   * Transient autosave status from the shared useIntakeAutosave hook (US_030, AC-3, EC-1).
   * Drives the "Auto-saved" flash indicator and failure caption.
   */
  autosaveStatus?: AutosaveStatus;
  /** When true the session is paused / connecting */
  isStarting?: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatAutoSaveLabel(lastSavedAt: string | null): string {
  if (!lastSavedAt) return '';
  const diffMs = Date.now() - new Date(lastSavedAt).getTime();
  const diffSec = Math.floor(diffMs / 1000);
  if (diffSec < 10) return 'Auto-saved just now';
  if (diffSec < 60) return `Auto-saved ${diffSec}s ago`;
  const diffMin = Math.floor(diffSec / 60);
  return `Auto-saved ${diffMin}m ago`;
}

// ─── Component ────────────────────────────────────────────────────────────────

function IntakeProgressHeader({
  collectedCount,
  totalRequired,
  lastSavedAt,
  autosaveStatus = 'idle',
  isStarting = false,
}: IntakeProgressHeaderProps) {
  const progressValue = useMemo(
    () => (totalRequired > 0 ? Math.round((collectedCount / totalRequired) * 100) : 0),
    [collectedCount, totalRequired],
  );

  // Freshness label shown when no transient status is active (UXR-004)
  const freshnessLabel = useMemo(() => formatAutoSaveLabel(lastSavedAt), [lastSavedAt]);

  // Determine which indicator to show (AC-3, EC-1)
  const showSaved    = autosaveStatus === 'saved';
  const showSaving   = autosaveStatus === 'saving' || autosaveStatus === 'retrying';
  const showFailed   = autosaveStatus === 'failed';
  const showFreshness = !showSaved && !showSaving && !showFailed && !!freshnessLabel;

  return (
    <Box
      role="status"
      aria-live="polite"
      aria-label={`Intake progress: ${collectedCount} of ${totalRequired} mandatory fields collected`}
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        px: 2,
        py: 1,
        bgcolor: 'grey.50',
        borderBottom: '1px solid',
        borderColor: 'grey.200',
        flexWrap: 'wrap',
        minHeight: 40,
      }}
    >
      {/* Field count label (AC-5) */}
      <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>
        {isStarting ? 'Starting…' : `Progress: ${collectedCount}/${totalRequired} mandatory fields`}
      </Typography>

      {/* Determinate progress bar */}
      <Box sx={{ flex: 1, maxWidth: 200, minWidth: 80 }} aria-hidden="true">
        <LinearProgress
          variant={isStarting ? 'indeterminate' : 'determinate'}
          value={progressValue}
          aria-hidden="true"
          sx={{
            height: 4,
            borderRadius: 2,
            bgcolor: 'grey.200',
            '& .MuiLinearProgress-bar': {
              bgcolor: 'primary.main',
              borderRadius: 2,
            },
          }}
        />
      </Box>

      {/* Transient "Auto-saved" flash — AC-3: brief appear-and-fade */}
      <Fade in={showSaved} timeout={{ enter: 200, exit: 800 }}>
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 0.5,
            color: 'success.main',
            whiteSpace: 'nowrap',
            visibility: showSaved ? 'visible' : 'hidden',
          }}
          aria-hidden={!showSaved}
        >
          <CheckCircleOutlineIcon sx={{ fontSize: 14 }} />
          <Typography variant="caption" color="success.main">
            Auto-saved
          </Typography>
        </Box>
      </Fade>

      {/* "Saving…" / "Retrying…" transient caption (AC-3) */}
      {showSaving && (
        <Typography variant="caption" color="text.disabled" sx={{ whiteSpace: 'nowrap' }}>
          {autosaveStatus === 'retrying' ? 'Retrying save…' : 'Saving…'}
        </Typography>
      )}

      {/* EC-1 failure caption — shown after both attempts fail */}
      {showFailed && (
        <Typography variant="caption" color="error" sx={{ whiteSpace: 'nowrap' }}>
          {AUTOSAVE_FAILED_MESSAGE}
        </Typography>
      )}

      {/* Freshness label (UXR-004) — shown when no transient status is active */}
      {showFreshness && (
        <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>
          {freshnessLabel}
        </Typography>
      )}
    </Box>
  );
}

export default memo(IntakeProgressHeader);
