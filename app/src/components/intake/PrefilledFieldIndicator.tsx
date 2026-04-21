/**
 * PrefilledFieldIndicator — visual badge indicating a field was pre-filled
 * from AI conversational intake (US_028 AC-2, SCR-009).
 *
 * Renders a small chip/helper below the field with "Pre-filled from AI intake"
 * using the info.surface color, keeping the field fully editable.
 *
 * Accessibility: icon is aria-hidden; meaningful text is always visible.
 * The parent field must NOT be disabled — only visually annotated.
 */

import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

interface PrefilledFieldIndicatorProps {
  /** When false the indicator is not rendered — simplifies conditional logic in parents. */
  visible?: boolean;
}

export default function PrefilledFieldIndicator({
  visible = true,
}: PrefilledFieldIndicatorProps) {
  if (!visible) return null;

  return (
    <Box
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 0.5,
        mt: 0.5,
        px: 1,
        py: 0.25,
        bgcolor: 'info.50',          // light info surface (design token: info.surface = #E1F5FE)
        borderRadius: 1,
        border: '1px solid',
        borderColor: 'info.light',
      }}
    >
      <AutoAwesomeIcon
        aria-hidden="true"
        sx={{ fontSize: '0.75rem', color: 'info.main' }}
      />
      <Typography
        variant="caption"
        sx={{ color: 'info.dark', lineHeight: 1.4 }}
      >
        Pre-filled from AI intake
      </Typography>
    </Box>
  );
}
