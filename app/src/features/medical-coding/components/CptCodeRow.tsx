/**
 * CptCodeRow — Table row for a single CPT procedure code suggestion (US_048 AC-2, AC-3).
 *
 * Per wireframe SCR-014 CPT table:
 *   Code (monospace bold) | Description | AI Suggested | Confidence | Status | Actions
 *
 * Actions per status:
 *   Pending   → "Approve" (primary) + "Override" (secondary) buttons (UXR-301)
 *   Approved  → "Override" button disabled (read-only row)
 *   Overridden → "By {name} on {date}" caption (audit trail display)
 *
 * Bundled procedures: MUI Chip "Bundled" label added alongside code value (AC-3).
 * Ambiguous procedures: reduced-confidence row is styled the same as other low-confidence rows.
 *
 * ARIA:
 *   - Approve/Override buttons have aria-label describing the target code.
 *   - Status badges have role="status" for screen-reader announcement.
 *   - Confidence badge has aria-label via ConfidenceBadge component.
 */

import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CheckIcon from '@mui/icons-material/Check';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import LinkIcon from '@mui/icons-material/Link';

import ConfidenceBadge from '@/components/coding/ConfidenceBadge';
import OverrideJustificationModal from './OverrideJustificationModal';
import type { CptCodeDto } from '../types/cpt.types';
import type { UseMutateAsyncFunction } from '@tanstack/react-query';
import type { CptApproveRequest, CptOverrideRequest } from '../types/cpt.types';

// ─── Status badge helpers ─────────────────────────────────────────────────────

function statusChipProps(status: CptCodeDto['status']): {
  label: string;
  bgcolor: string;
  color: string;
} {
  switch (status) {
    case 'Approved':
      return { label: 'Approved', bgcolor: 'success.main', color: '#fff' };
    case 'Overridden':
      return { label: 'Overridden', bgcolor: 'error.main', color: '#fff' };
    case 'Pending':
    default:
      return { label: 'Pending Review', bgcolor: 'warning.main', color: '#fff' };
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface CptCodeRowProps {
  code:      CptCodeDto;
  rank:      number;
  isEvenRow: boolean;
  onApprove: UseMutateAsyncFunction<void, unknown, CptApproveRequest, unknown>;
  onOverride: UseMutateAsyncFunction<void, unknown, CptOverrideRequest, unknown>;
  approveLoading: boolean;
  overrideLoading: boolean;
  /** Optional payer validation status for this code (US_051). */
  payerStatus?: 'valid' | 'warning' | 'denial-risk';
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CptCodeRow({
  code,
  rank,
  isEvenRow,
  onApprove,
  onOverride,
  approveLoading,
  overrideLoading,
  payerStatus,
}: CptCodeRowProps) {
  const [modalOpen,   setModalOpen]   = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const statusBadge = statusChipProps(code.status);
  const codeLabel   = `${code.codeValue}${code.description ? ` — ${code.description}` : ''}`;

  async function handleApprove() {
    if (!code.medicalCodeId) return;
    await onApprove({ medicalCodeId: code.medicalCodeId });
  }

  async function handleOverrideSubmit({
    replacementCode,
    justification,
  }: { replacementCode: string; justification: string }) {
    if (!code.medicalCodeId) return;
    setSubmitError(null);
    try {
      await onOverride({
        medicalCodeId:   code.medicalCodeId,
        replacementCode,
        justification,
      });
      setModalOpen(false);
    } catch {
      setSubmitError('Override failed — please try again.');
    }
  }

  return (
    <>
      <TableRow
        sx={{
          bgcolor:         isEvenRow ? 'grey.50' : 'transparent',
          minHeight:       52,
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
              }}
            >
              {code.codeValue}
            </Typography>
            {code.isBundled && (
              <Chip
                icon={<LinkIcon />}
                label="Bundled"
                size="small"
                color="info"
                variant="outlined"
                aria-label="Bundled procedure code"
                sx={{ fontSize: '0.625rem', height: 20 }}
              />
            )}
          </Box>
        </TableCell>

        {/* ── Description ── */}
        <TableCell>
          <Typography variant="body2">
            {code.description || '—'}
          </Typography>
        </TableCell>

        {/* ── AI Suggested ── */}
        <TableCell>
          <Typography
            component="span"
            sx={{
              fontFamily: 'Roboto Mono, Courier New, monospace',
              fontSize:   '0.8125rem',
              color:      'text.secondary',
            }}
          >
            {code.codeValue}
          </Typography>
        </TableCell>

        {/* ── Confidence ── */}
        <TableCell sx={{ width: 100 }}>
          <ConfidenceBadge score={code.confidenceScore} />
        </TableCell>

        {/* ── Status ── */}
        <TableCell sx={{ width: 130 }}>
          <Box
            component="span"
            role="status"
            aria-label={`Code status: ${statusBadge.label}`}
            sx={{
              display:       'inline-flex',
              alignItems:    'center',
              bgcolor:       statusBadge.bgcolor,
              color:         statusBadge.color,
              borderRadius:  '9999px',
              px:            1.25,
              py:            0.375,
              fontSize:      '0.6875rem',
              fontWeight:    600,
              letterSpacing: '0.04em',
              textTransform: 'uppercase',
              whiteSpace:    'nowrap',
            }}
          >
            {statusBadge.label}
          </Box>
        </TableCell>

        {/* ── Actions ── */}
        <TableCell sx={{ width: 180, whiteSpace: 'nowrap' }}>
          {code.status === 'Overridden' ? (
            /* Audit trail display */
            <Typography variant="caption" color="text.secondary">
              {code.overriddenByName
                ? `By ${code.overriddenByName}${code.overriddenAt ? ` on ${new Date(code.overriddenAt).toLocaleDateString()}` : ''}`
                : 'Overridden'}
            </Typography>
          ) : code.status === 'Approved' ? (
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <Tooltip title="Code approved">
                <CheckIcon fontSize="small" color="success" aria-label="Code approved" />
              </Tooltip>
              <Button
                size="small"
                variant="outlined"
                color="inherit"
                disabled
                aria-label={`Override ${code.codeValue} (already approved)`}
                sx={{ minHeight: 32 }}
              >
                Override
              </Button>
            </Box>
          ) : (
            /* Pending — show Approve + Override */
            <Box sx={{ display: 'flex', gap: 1 }}>
              <Button
                size="small"
                variant="contained"
                color="primary"
                onClick={handleApprove}
                disabled={approveLoading || !code.medicalCodeId}
                aria-label={`Approve code ${code.codeValue}`}
                sx={{ minHeight: 32 }}
                startIcon={approveLoading ? <CircularProgress size={12} color="inherit" /> : null}
              >
                Approve
              </Button>
              <Button
                size="small"
                variant="outlined"
                color="inherit"
                onClick={() => setModalOpen(true)}
                disabled={overrideLoading || !code.medicalCodeId}
                aria-label={`Override code ${code.codeValue}`}
                sx={{ minHeight: 32 }}
              >
                Override
              </Button>
            </Box>
          )}
        </TableCell>

        {/* ── Justification (hidden at sm) ── */}
        <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, maxWidth: 280 }}>
          {code.justification ? (
            <Tooltip
              title={code.justification}
              arrow
              placement="top"
              componentsProps={{
                tooltip: { sx: { maxWidth: 300, fontSize: '0.75rem' } },
              }}
            >
              <Box
                sx={{
                  display:           '-webkit-box',
                  WebkitLineClamp:   2,
                  WebkitBoxOrient:   'vertical',
                  overflow:          'hidden',
                  cursor:            'default',
                  fontSize:          '0.8125rem',
                  color:             'text.secondary',
                }}
                tabIndex={0}
                aria-label={`Justification: ${code.justification}`}
              >
                <InfoOutlinedIcon
                  fontSize="inherit"
                  sx={{ mr: 0.5, verticalAlign: 'middle', color: 'text.disabled' }}
                />
                {code.justification}
              </Box>
            </Tooltip>
          ) : (
            <Typography variant="caption" color="text.disabled">—</Typography>
          )}
        </TableCell>

        {/* ── Payer Status (optional, US_051) ── */}
        {payerStatus !== undefined && (
          <TableCell sx={{ width: 110 }}>
            {payerStatus === 'denial-risk' && (
              <Chip label="Denial Risk" size="small" color="error"   variant="outlined" sx={{ fontSize: '0.65rem', height: 20 }} aria-label="Payer status: denial risk" />
            )}
            {payerStatus === 'warning' && (
              <Chip label="Review"     size="small" color="warning" variant="outlined" sx={{ fontSize: '0.65rem', height: 20 }} aria-label="Payer status: review recommended" />
            )}
            {payerStatus === 'valid' && (
              <Chip label="Valid"      size="small" color="success" variant="outlined" sx={{ fontSize: '0.65rem', height: 20 }} aria-label="Payer status: valid" />
            )}
          </TableCell>
        )}
      </TableRow>

      {/* ── Override modal (rendered outside table flow to avoid nesting issues) ── */}
      <OverrideJustificationModal
        open={modalOpen}
        codeLabel={codeLabel}
        isSubmitting={overrideLoading}
        submitError={submitError}
        onClose={() => { setModalOpen(false); setSubmitError(null); }}
        onSubmit={handleOverrideSubmit}
      />
    </>
  );
}
