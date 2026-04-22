/**
 * ConflictValueSelector — MUI RadioGroup allowing staff to select the correct data
 * value from conflicting source citations, or choose "Both Valid — Different Dates"
 * when both values are clinically correct (US_045 AC-2, EC-2, UXR-104).
 *
 * Design tokens:
 *   Radio checked:   primary.500
 *   Radio unchecked: neutral.400
 *   Radio icon size: 24×24 px
 *   Touch target:    44px min height (WCAG 2.2 SC 2.5.8)
 *   Both Valid option: styled with info.main border to distinguish from normal selection
 *
 * ARIA: role="radiogroup", aria-label="Select the correct data value",
 *       each option role="radio" (provided by MUI Radio).
 */

import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';
import Divider from '@mui/material/Divider';
import EventAvailableIcon from '@mui/icons-material/EventAvailable';

import type { ConflictSourceCitationDto } from '@/hooks/useConflictDetail';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Sentinel value used when staff selects the "Both Valid" option. */
export const BOTH_VALID_VALUE = '__both_valid__';

export interface ConflictValueSelectorProps {
  citations: ConflictSourceCitationDto[];
  /** Currently selected extractedDataId, or BOTH_VALID_VALUE, or null (nothing selected). */
  selectedValue: string | null;
  onChange: (value: string) => void;
  disabled?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConflictValueSelector({
  citations,
  selectedValue,
  onChange,
  disabled = false,
}: ConflictValueSelectorProps) {
  return (
    <FormControl component="fieldset" fullWidth disabled={disabled}>
      <FormLabel
        component="legend"
        sx={{
          typography: 'subtitle2',
          color: 'text.primary',
          fontWeight: 600,
          mb: 1,
          '&.Mui-focused': { color: 'text.primary' },
        }}
      >
        Select the correct value
      </FormLabel>

      <RadioGroup
        aria-label="Select the correct data value"
        value={selectedValue ?? ''}
        onChange={(_e, val) => onChange(val)}
      >
        {citations.map((citation, idx) => {
          const label = citation.normalizedValue ?? citation.rawText ?? 'No value extracted';
          const sublabel = `${citation.documentName} · ${new Date(citation.uploadDate).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })}`;

          return (
            <FormControlLabel
              key={citation.extractedDataId}
              value={citation.extractedDataId}
              control={
                <Radio
                  sx={{
                    p: 0.5,
                    '& .MuiSvgIcon-root': { fontSize: 24 },
                    color: 'neutral.400',
                    '&.Mui-checked': { color: 'primary.500' },
                  }}
                  inputProps={{
                    'aria-label': `Source ${idx + 1}: ${label} from ${citation.documentName}`,
                  }}
                />
              }
              label={
                <Box sx={{ py: 0.5 }}>
                  <Typography variant="body2" fontWeight={600}>
                    {label}
                    {citation.unit && (
                      <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 0.5 }}>
                        {citation.unit}
                      </Typography>
                    )}
                  </Typography>
                  <Typography variant="caption" color="text.secondary" display="block">
                    {sublabel}
                  </Typography>
                </Box>
              }
              sx={{
                minHeight: 44, // WCAG 2.2 touch target
                alignItems: 'flex-start',
                mx: 0,
                mb: 0.5,
                borderRadius: 1,
                px: 1,
                transition: 'background-color 150ms',
                '&:hover': { bgcolor: 'action.hover' },
                ...(selectedValue === citation.extractedDataId && {
                  bgcolor: 'primary.50',
                  '&:hover': { bgcolor: 'primary.50' },
                }),
              }}
            />
          );
        })}

        {/* Divider + "Both Valid" option (EC-2) */}
        {citations.length >= 2 && (
          <>
            <Divider sx={{ my: 1 }} />
            <FormControlLabel
              value={BOTH_VALID_VALUE}
              control={
                <Radio
                  sx={{
                    p: 0.5,
                    '& .MuiSvgIcon-root': { fontSize: 24 },
                    color: 'neutral.400',
                    '&.Mui-checked': { color: 'info.main' },
                  }}
                  inputProps={{ 'aria-label': 'Both values are valid with different dates' }}
                />
              }
              label={
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, py: 0.5 }}>
                  <EventAvailableIcon fontSize="small" sx={{ color: 'info.main' }} aria-hidden />
                  <Box>
                    <Typography variant="body2" fontWeight={600} color="info.dark">
                      Both Valid — Different Dates
                    </Typography>
                    <Typography variant="caption" color="text.secondary" display="block">
                      Both entries are clinically correct with distinct date attribution
                    </Typography>
                  </Box>
                </Box>
              }
              sx={{
                minHeight: 44,
                alignItems: 'flex-start',
                mx: 0,
                borderRadius: 1,
                px: 1,
                border: '1px solid',
                borderColor: selectedValue === BOTH_VALID_VALUE ? 'info.main' : 'transparent',
                transition: 'all 150ms',
                '&:hover': { bgcolor: 'info.50', borderColor: 'info.light' },
                ...(selectedValue === BOTH_VALID_VALUE && {
                  bgcolor: 'info.50',
                }),
              }}
            />
          </>
        )}
      </RadioGroup>
    </FormControl>
  );
}
