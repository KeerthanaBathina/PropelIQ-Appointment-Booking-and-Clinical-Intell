/**
 * BundlingRuleWarning — Alert component for NCCI bundling rule violations.
 * (US_051, AC-4, SCR-014)
 *
 * Shown after all codes are verified and bundling check runs. Displays:
 *   • Violating code pair (column1 / column2 in monospace chips)
 *   • CCI edit type (Column1/Column2)
 *   • Required modifiers to allow separate billing (e.g. Mod. 59, Mod. 25)
 *   • Violation description
 *
 * When no violations exist a subtle success indicator is shown (all codes valid).
 * Hidden entirely when bundling check has not yet run (violations = undefined).
 *
 * UXR-105: modifier chips use primary color, code chips use monospace.
 * Design tokens: error-500 #D32F2F for violations, success-main for clear.
 */

import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import type { BundlingRuleResultDto } from '@/hooks/usePayerValidation';

// ─── Props ────────────────────────────────────────────────────────────────────

interface BundlingRuleWarningProps {
  /** Bundling violations; undefined = check not yet run, [] = all clear. */
  violations:  BundlingRuleResultDto[] | undefined;
  isLoading?:  boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BundlingRuleWarning({
  violations,
  isLoading = false,
}: BundlingRuleWarningProps) {
  // Not yet run
  if (violations === undefined || isLoading) return null;

  // All clear
  if (violations.length === 0) {
    return (
      <Alert
        severity="success"
        icon={<CheckCircleOutlineIcon fontSize="inherit" />}
        role="status"
        sx={{ mb: 2 }}
      >
        All codes passed bundling rule validation — no NCCI edit violations detected.
      </Alert>
    );
  }

  return (
    <Box component="section" aria-labelledby="bundling-warning-heading" sx={{ mb: 2 }}>
      <Alert
        severity="error"
        role="alert"
        aria-live="assertive"
        sx={{ alignItems: 'flex-start' }}
      >
        <AlertTitle id="bundling-warning-heading">
          Bundling Rule Violations Detected
        </AlertTitle>
        <Typography variant="body2" sx={{ mb: 1.5 }}>
          {violations.length} code pair{violations.length === 1 ? '' : 's'} violate{violations.length === 1 ? 's' : ''} CMS NCCI bundling edits and cannot be billed
          together without an appropriate modifier.
        </Typography>

        {violations.map((v, idx) => (
          <Box key={idx}>
            {idx > 0 && <Divider sx={{ my: 1 }} />}

            {/* Code pair */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', mb: 0.75 }}>
              <Chip
                label={v.column1_code}
                size="small"
                variant="outlined"
                sx={{ fontFamily: 'monospace', fontWeight: 700, fontSize: '0.75rem' }}
                aria-label={`Column 1 code: ${v.column1_code}`}
              />
              <Typography variant="caption" color="text.secondary">+</Typography>
              <Chip
                label={v.column2_code}
                size="small"
                variant="outlined"
                sx={{ fontFamily: 'monospace', fontWeight: 700, fontSize: '0.75rem' }}
                aria-label={`Column 2 code: ${v.column2_code}`}
              />
              <Chip
                label={`CCI: ${v.cci_edit_type}`}
                size="small"
                color="default"
                variant="outlined"
                sx={{ fontSize: '0.65rem', height: 18 }}
              />
            </Box>

            {/* Description */}
            <Typography variant="caption" display="block" sx={{ mb: 0.75 }}>
              {v.description}
            </Typography>

            {/* Required modifiers */}
            {v.required_modifiers.length > 0 && (
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
                <Typography variant="caption" color="text.secondary" fontWeight={600}>
                  Apply modifier:
                </Typography>
                {v.required_modifiers.map(mod => (
                  <Chip
                    key={mod}
                    label={`Mod. ${mod}`}
                    size="small"
                    color="primary"
                    variant="outlined"
                    sx={{ fontSize: '0.7rem', height: 20 }}
                    aria-label={`Required modifier: ${mod}`}
                  />
                ))}
              </Box>
            )}
          </Box>
        ))}
      </Alert>
    </Box>
  );
}
