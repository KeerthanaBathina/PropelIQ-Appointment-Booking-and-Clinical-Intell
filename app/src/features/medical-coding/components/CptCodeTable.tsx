/**
 * CptCodeTable — Sortable MUI table displaying AI-generated CPT procedure code
 * suggestions with Approve/Override workflow (US_048 AC-2, AC-3; SCR-014).
 *
 * Columns: # | Code | Description | AI Suggested | Confidence | Status | Actions | Justification
 *
 * 5 required screen states:
 *   1. Loading    — Skeleton rows matching table columns (UXR-502)
 *   2. Error      — Alert + retry button (UXR-402)
 *   3. Empty      — "No CPT code suggestions yet" message (UXR-403)
 *   4. Default    — Sorted table with ConfidenceBadge + action buttons
 *   5. Validation — Inline justification errors surfaced via OverrideJustificationModal
 *
 * Bundled procedure groups (AC-3): rows with the same bundleGroupId are grouped and
 * display a "Bundled" Chip indicator. Both the bundled code and individual components
 * are shown, ranked by relevance score.
 *
 * Table header tokens (designsystem.md):
 *   Background: grey.100 (neutral.100), Typography: subtitle1 600
 * Row tokens:
 *   min-height 52px, alternating grey.50 stripe, body2 typography
 */

import { useCallback, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import Typography from '@mui/material/Typography';
import MedicalServicesIcon from '@mui/icons-material/MedicalServices';
import { visuallyHidden } from '@mui/utils';

import CptCodeRow from './CptCodeRow';
import type { CptCodeDto } from '../types/cpt.types';
import { useCptApprove } from '../hooks/useCptApprove';
import { useCptOverride } from '../hooks/useCptOverride';

// ─── Types ────────────────────────────────────────────────────────────────────

type SortColumn    = 'relevanceRank' | 'confidenceScore';
type SortDirection = 'asc' | 'desc';

// ─── Props ────────────────────────────────────────────────────────────────────

interface CptCodeTableProps {
  codes:     CptCodeDto[];
  patientId: string;
  isLoading: boolean;
  isError:   boolean;
  onRetry:   () => void;
  /** Optional map from codeValue → payer status badge (US_051). Omit when validation not yet run. */
  payerStatusByCode?: Record<string, 'valid' | 'warning' | 'denial-risk'>;
}

// ─── Skeleton rows ────────────────────────────────────────────────────────────

function LoadingRows() {
  return (
    <>
      {Array.from({ length: 4 }).map((_, i) => (
        <TableRow key={i} sx={{ minHeight: 52 }}>
          <TableCell><Skeleton variant="text" width={24} /></TableCell>
          <TableCell><Skeleton variant="text" width={56} /></TableCell>
          <TableCell><Skeleton variant="text" width={200} /></TableCell>
          <TableCell><Skeleton variant="text" width={56} /></TableCell>
          <TableCell><Skeleton variant="rounded" width={44} height={20} sx={{ borderRadius: '9999px' }} /></TableCell>
          <TableCell><Skeleton variant="rounded" width={80} height={20} sx={{ borderRadius: '9999px' }} /></TableCell>
          <TableCell><Skeleton variant="text" width={130} /></TableCell>
          <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>
            <Skeleton variant="text" width={160} />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CptCodeTable({
  codes,
  patientId,
  isLoading,
  isError,
  onRetry,
  payerStatusByCode,
}: CptCodeTableProps) {
  const [sortColumn,    setSortColumn]    = useState<SortColumn>('relevanceRank');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');

  const approveMutation = useCptApprove(patientId);
  const overrideMutation = useCptOverride(patientId);

  const handleSort = useCallback((col: SortColumn) => {
    setSortColumn(prev => {
      if (prev === col) {
        setSortDirection(d => d === 'asc' ? 'desc' : 'asc');
        return prev;
      }
      setSortDirection('asc');
      return col;
    });
  }, []);

  const sortedCodes = useMemo(() => {
    return [...codes].sort((a, b) => {
      const aVal = sortColumn === 'relevanceRank'
        ? (a.relevanceRank ?? 9999)
        : a.confidenceScore;
      const bVal = sortColumn === 'relevanceRank'
        ? (b.relevanceRank ?? 9999)
        : b.confidenceScore;
      const factor = sortDirection === 'asc' ? 1 : -1;
      return (aVal - bVal) * factor;
    });
  }, [codes, sortColumn, sortDirection]);

  // ── Error state ──────────────────────────────────────────────────────────
  if (isError) {
    return (
      <Alert
        severity="error"
        role="alert"
        action={<Button color="inherit" size="small" onClick={onRetry}>Retry</Button>}
        sx={{ mb: 2 }}
      >
        Failed to load CPT procedure code suggestions. Check your connection and try again.
      </Alert>
    );
  }

  // ── Empty state ──────────────────────────────────────────────────────────
  if (!isLoading && codes.length === 0) {
    return (
      <Box
        sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', py: 8, gap: 2 }}
        role="status"
        aria-label="No CPT procedure code suggestions available"
      >
        <MedicalServicesIcon sx={{ fontSize: 56, color: 'text.disabled' }} />
        <Typography variant="h6" color="text.secondary">
          No CPT code suggestions yet
        </Typography>
        <Typography variant="body2" color="text.disabled" textAlign="center" maxWidth={360}>
          CPT procedure codes will appear here after the AI coding pipeline has processed
          this patient's clinical data.
        </Typography>
      </Box>
    );
  }

  // ── Default + Loading states ──────────────────────────────────────────────
  return (
    <TableContainer
      component={Paper}
      variant="outlined"
      aria-label="CPT procedure code suggestions table"
      sx={{ borderRadius: 2 }}
    >
      <Table size="medium" aria-describedby="cpt-table-desc">
        <caption id="cpt-table-desc" style={visuallyHidden}>
          AI-generated CPT procedure code suggestions sorted by{' '}
          {sortColumn === 'relevanceRank' ? 'relevance' : 'confidence'},{' '}
          {sortDirection === 'asc' ? 'ascending' : 'descending'}.
        </caption>

        {/* ── Table Head ── */}
        <TableHead>
          <TableRow sx={{ bgcolor: 'grey.100' }}>
            {/* Rank */}
            <TableCell sx={{ width: 40 }}>
              <TableSortLabel
                active={sortColumn === 'relevanceRank'}
                direction={sortColumn === 'relevanceRank' ? sortDirection : 'asc'}
                onClick={() => handleSort('relevanceRank')}
                aria-sort={sortColumn === 'relevanceRank' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}
              >
                <Typography variant="subtitle1" component="span" fontWeight={600} fontSize="0.75rem">
                  #
                </Typography>
              </TableSortLabel>
            </TableCell>

            {/* Code */}
            <TableCell>
              <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                Code
              </Typography>
            </TableCell>

            {/* Description */}
            <TableCell>
              <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                Description
              </Typography>
            </TableCell>

            {/* AI Suggested */}
            <TableCell>
              <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                AI Suggested
              </Typography>
            </TableCell>

            {/* Confidence */}
            <TableCell sx={{ width: 110 }}>
              <TableSortLabel
                active={sortColumn === 'confidenceScore'}
                direction={sortColumn === 'confidenceScore' ? sortDirection : 'desc'}
                onClick={() => handleSort('confidenceScore')}
                aria-sort={sortColumn === 'confidenceScore' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}
              >
                <Typography variant="subtitle1" component="span" fontWeight={600} fontSize="0.8125rem">
                  Confidence
                </Typography>
              </TableSortLabel>
            </TableCell>

            {/* Status */}
            <TableCell sx={{ width: 130 }}>
              <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                Status
              </Typography>
            </TableCell>

            {/* Actions */}
            <TableCell sx={{ width: 180 }}>
              <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                Actions
              </Typography>
            </TableCell>

            {/* Justification (hidden at sm) */}
            <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, maxWidth: 280 }}>
              <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                Justification
              </Typography>
            </TableCell>

            {/* Payer Status (optional, US_051) */}
            {payerStatusByCode && (
              <TableCell sx={{ width: 110 }}>
                <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                  Payer
                </Typography>
              </TableCell>
            )}
          </TableRow>
        </TableHead>

        {/* ── Table Body ── */}
        <TableBody>
          {isLoading ? (
            <LoadingRows />
          ) : (
            sortedCodes.map((code, idx) => (
              <CptCodeRow
                key={code.medicalCodeId ?? `${code.codeValue}-${idx}`}
                code={code}
                rank={idx + 1}
                isEvenRow={idx % 2 === 0}
                onApprove={approveMutation.mutateAsync}
                onOverride={overrideMutation.mutateAsync}
                approveLoading={approveMutation.isLoading}
                overrideLoading={overrideMutation.isLoading}                payerStatus={payerStatusByCode?.[code.codeValue]}              />
            ))
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
