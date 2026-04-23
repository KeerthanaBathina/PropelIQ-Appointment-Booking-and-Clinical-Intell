/**
 * DeprecatedCodeWarning — inline warning chip for MedicalCode records where
 * revalidation_status = "DeprecatedReplaced" (US_047 edge case, AC-3).
 *
 * Displays a MUI Chip with warning colour indicating the ICD-10 code was
 * deprecated after a library refresh, plus a tooltip explaining the situation
 * so staff can take corrective action.
 *
 * Design tokens: warning.main (#ED6C02), chip border-radius (16px = radius.xl).
 */

import Chip from '@mui/material/Chip';
import Tooltip from '@mui/material/Tooltip';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';

// ─── Props ────────────────────────────────────────────────────────────────────

interface DeprecatedCodeWarningProps {
  /** validationStatus from Icd10CodeDto — only renders when "DeprecatedReplaced". */
  validationStatus: string | null;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DeprecatedCodeWarning({
  validationStatus,
}: DeprecatedCodeWarningProps) {
  if (validationStatus !== 'DeprecatedReplaced') return null;

  return (
    <Tooltip
      title="This code was deprecated after a library update. Please review and select a replacement code."
      arrow
      placement="top"
    >
      <Chip
        icon={<WarningAmberIcon />}
        label="Deprecated"
        color="warning"
        size="small"
        aria-label="Code deprecated — review required"
        sx={{
          fontWeight:    600,
          fontSize:      '0.625rem',
          textTransform: 'uppercase',
          letterSpacing: '0.08333em',
          borderRadius:  '16px',
          height:        20,
          '& .MuiChip-icon': { fontSize: '0.875rem' },
        }}
      />
    </Tooltip>
  );
}
