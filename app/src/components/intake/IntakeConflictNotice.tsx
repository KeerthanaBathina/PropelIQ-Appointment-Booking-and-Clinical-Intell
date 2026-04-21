/**
 * IntakeConflictNotice — review-state indicator for mode-switch value conflicts (US_029, EC-1).
 *
 * Shown inside the AI intake summary review table when a field has been updated by a later
 * source (manual or AI) overriding an earlier value. The most recent value is displayed as
 * active; this component surfaces the overridden source so the patient retains full visibility
 * (AC-4: merged data from all modes with correct source attribution).
 *
 * Design intent:
 *   - Non-blocking: presented as a small inline info note, never as an error or modal.
 *   - Accessible: tooltip carries the alternate value text; screen readers announce via
 *     `title` attribute on the icon container.
 *   - Compact: uses `caption` typography and 0.9rem icon to avoid dominating the table row.
 */

import Box from '@mui/material/Box';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';

// ─── Types ────────────────────────────────────────────────────────────────────

interface IntakeConflictNoticeProps {
  /** The value from the other (overridden) source. Displayed in the tooltip. */
  alternateValue: string;
  /** Which source was overridden ('ai' or 'manual'). Used in the label text. */
  overriddenSource: 'ai' | 'manual';
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function IntakeConflictNotice({
  alternateValue,
  overriddenSource,
}: IntakeConflictNoticeProps) {
  const sourceLabel = overriddenSource === 'ai' ? 'AI intake' : 'manual form';

  return (
    <Tooltip
      title={`${sourceLabel} value: "${alternateValue}"`}
      arrow
      placement="right"
    >
      <Box
        component="span"
        sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5, cursor: 'help', mt: 0.25 }}
        role="note"
        aria-label={`Overrides ${sourceLabel} value: ${alternateValue}`}
      >
        <InfoOutlinedIcon sx={{ fontSize: '0.9rem', color: 'warning.main' }} aria-hidden="true" />
        <Typography
          variant="caption"
          color="warning.dark"
          sx={{ fontStyle: 'italic', lineHeight: 1.2 }}
        >
          Overrides {sourceLabel} value
        </Typography>
      </Box>
    </Tooltip>
  );
}
