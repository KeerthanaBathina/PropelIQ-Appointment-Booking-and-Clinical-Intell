/**
 * Icd10CodeRow — Table row displaying a single ICD-10 code suggestion (US_047 AC-2, AC-4).
 *
 * Displays per wireframe SCR-014:
 *   Code value  (monospace, bold)
 *   Description (body2)
 *   ConfidenceBadge (colour-coded pill, UXR-105)
 *   Justification (expandable tooltip, max 300px — designsystem.md Tooltip spec)
 *   Relevance rank
 *   Status / validation badges
 *
 * Deprecated codes (revalidation_status = "DeprecatedReplaced") render an inline
 * DeprecatedCodeWarning chip per the task spec and edge case.
 *
 * Uncodable rows (codeValue = "UNCODABLE") are styled with warning colour so
 * staff can identify them at a glance — the UncodableAlert above the table
 * provides the full call-to-action.
 *
 * Responsive:
 *   1440px — all columns visible.
 *   768px  — justification column hidden (accessible via expandable row).
 *   375px  — rendered as a Card by Icd10CodeTable (this component not used directly).
 */

import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';

import ConfidenceBadge from './ConfidenceBadge';
import DeprecatedCodeWarning from './DeprecatedCodeWarning';
import type { Icd10CodeDto } from '@/hooks/useIcd10Codes';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Icd10CodeRowProps {
  code: Icd10CodeDto;
  /** Rank among all codes for the same diagnosis (1-based). */
  rank: number;
  hideJustificationColumn?: boolean;
  /** Optional payer validation status for this code — shown when provided (US_051). */
  payerStatus?: 'valid' | 'warning' | 'denial-risk';
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function Icd10CodeRow({
  code,
  rank,
  hideJustificationColumn = false,
  payerStatus,
}: Icd10CodeRowProps) {
  const isUncodable   = code.codeValue === 'UNCODABLE';
  const isDeprecated  = code.validationStatus === 'DeprecatedReplaced';
  const isEvenRow     = rank % 2 === 0;

  return (
    <TableRow
      sx={{
        bgcolor:         isEvenRow ? 'grey.50' : 'transparent',
        minHeight:       52,
        opacity:         isUncodable ? 0.75 : 1,
        '&:hover':       { bgcolor: 'action.hover' },
        '&:focus-within': { outline: '2px solid', outlineColor: 'primary.500', outlineOffset: -2 },
      }}
    >
      {/* ── Rank ── */}
      <TableCell sx={{ width: 40, color: 'text.secondary', fontSize: '0.75rem' }}>
        #{rank}
      </TableCell>

      {/* ── Code value ── */}
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Typography
            component="span"
            sx={{
              fontFamily: 'Roboto Mono, Courier New, monospace',
              fontWeight: 700,
              fontSize:   '0.875rem',
              color:      isUncodable ? 'warning.main' : 'text.primary',
            }}
          >
            {code.codeValue}
          </Typography>
          {isDeprecated && <DeprecatedCodeWarning validationStatus={code.validationStatus} />}
        </Box>
      </TableCell>

      {/* ── Description ── */}
      <TableCell>
        <Typography variant="body2" color={isUncodable ? 'warning.main' : 'text.primary'}>
          {code.description || '—'}
        </Typography>
        {code.libraryVersion && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            v{code.libraryVersion}
          </Typography>
        )}
      </TableCell>

      {/* ── Confidence ── */}
      <TableCell sx={{ width: 100 }}>
        {isUncodable
          ? <Typography variant="caption" color="warning.main">Uncodable</Typography>
          : <ConfidenceBadge score={code.confidenceScore} />}
      </TableCell>

      {/* ── Justification (hidden at 768px) ── */}
      {!hideJustificationColumn && (
        <TableCell sx={{ maxWidth: 320 }}>
          {code.justification ? (
            <Tooltip
              title={code.justification}
              arrow
              placement="top"
              componentsProps={{
                tooltip: {
                  sx: { maxWidth: 300, fontSize: '0.75rem' },
                },
              }}
            >
              <Box
                sx={{
                  display:       '-webkit-box',
                  WebkitLineClamp: 2,
                  WebkitBoxOrient: 'vertical',
                  overflow:      'hidden',
                  textOverflow:  'ellipsis',
                  cursor:        'default',
                  fontSize:      '0.875rem',
                }}
                aria-label={`Justification: ${code.justification}`}
                tabIndex={0}
              >
                <InfoOutlinedIcon
                  fontSize="inherit"
                  sx={{ mr: 0.5, verticalAlign: 'middle', color: 'text.secondary' }}
                />
                {code.justification}
              </Box>
            </Tooltip>
          ) : (
            <Typography variant="caption" color="text.disabled">—</Typography>
          )}
        </TableCell>
      )}

      {/* ── Status ── */}
      <TableCell sx={{ width: 120 }}>
        {code.requiresReview ? (
          <Typography
            component="span"
            sx={{
              fontSize:      '0.625rem',
              fontWeight:    500,
              textTransform: 'uppercase',
              letterSpacing: '0.08333em',
              bgcolor:       'warning.surface',
              color:         'warning.dark',
              borderRadius:  '9999px',
              px:            1,
              py:            0.25,
            }}
          >
            Review
          </Typography>
        ) : (
          <Typography variant="caption" color="text.secondary">—</Typography>
        )}
      </TableCell>

      {/* ── Payer Status (optional, US_051) ── */}
      {payerStatus !== undefined && (
        <TableCell sx={{ width: 110 }}>
          {payerStatus === 'denial-risk' && (
            <Chip
              label="Denial Risk"
              size="small"
              color="error"
              variant="outlined"
              sx={{ fontSize: '0.65rem', height: 20 }}
              aria-label="Payer status: denial risk"
            />
          )}
          {payerStatus === 'warning' && (
            <Chip
              label="Review"
              size="small"
              color="warning"
              variant="outlined"
              sx={{ fontSize: '0.65rem', height: 20 }}
              aria-label="Payer status: review recommended"
            />
          )}
          {payerStatus === 'valid' && (
            <Chip
              label="Valid"
              size="small"
              color="success"
              variant="outlined"
              sx={{ fontSize: '0.65rem', height: 20 }}
              aria-label="Payer status: valid"
            />
          )}
        </TableCell>
      )}
    </TableRow>
  );
}
