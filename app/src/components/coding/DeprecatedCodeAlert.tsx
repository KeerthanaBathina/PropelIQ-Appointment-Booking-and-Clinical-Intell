/**
 * DeprecatedCodeAlert — MUI Alert shown when staff attempts to approve a
 * deprecated code and the API returns 409 Conflict (US_049 EC-1, FR-049).
 *
 * Displays the deprecated_notice from the API response and a list of clickable
 * replacement code chips. Clicking a chip triggers CodeOverrideModal pre-filled
 * with that replacement code value.
 *
 * Severity: "warning" — matches the amber deprecated state in the wireframe.
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

// ─── Props ────────────────────────────────────────────────────────────────────

interface DeprecatedCodeAlertProps {
  /** Deprecation notice text from the 409 Conflict response. */
  deprecatedNotice:    string;
  /** Suggested replacement code values (e.g. ["E11.65", "E11.649"]). */
  replacementCodes:    string[];
  /** Called with the selected replacement code value when a chip is clicked. */
  onSelectReplacement: (code: string) => void;
  /** Called when the alert is dismissed. */
  onDismiss:           () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DeprecatedCodeAlert({
  deprecatedNotice,
  replacementCodes,
  onSelectReplacement,
  onDismiss,
}: DeprecatedCodeAlertProps) {
  return (
    <Alert
      severity="warning"
      role="alert"
      onClose={onDismiss}
      sx={{ mb: 1 }}
    >
      <Typography variant="body2" gutterBottom>
        {deprecatedNotice || 'This code has been deprecated. Please select a replacement.'}
      </Typography>

      {replacementCodes.length > 0 && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', mt: 0.5 }}>
          <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>
            Select replacement:
          </Typography>
          {replacementCodes.map(code => (
            <Chip
              key={code}
              label={code}
              size="small"
              clickable
              color="warning"
              variant="outlined"
              onClick={() => onSelectReplacement(code)}
              aria-label={`Use replacement code ${code}`}
            />
          ))}
        </Box>
      )}
    </Alert>
  );
}
