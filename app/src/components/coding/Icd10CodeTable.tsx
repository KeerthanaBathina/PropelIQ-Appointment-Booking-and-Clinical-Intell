/**
 * Icd10CodeTable — Sortable MUI table displaying AI-generated ICD-10 code
 * suggestions for a patient encounter (US_047 AC-1, AC-2, AC-4; SCR-014).
 *
 * 5 required states (designsystem.md § Screen States):
 *   1. Loading    — Skeleton rows matching table columns (UXR-401)
 *   2. Error      — Alert + retry button (UXR-402)
 *   3. Empty      — "No code suggestions yet" with request CTA (UXR-403)
 *   4. Default    — Sorted table with ConfidenceBadge + Deprecated warnings
 *   5. Validation — UncodableAlert visible above table when UNCODABLE codes present
 *
 * Table headers: grey.100 background + subtitle1 typography per designsystem.md.
 * Row stripes: neutral.50 / transparent alternating (grey.50) per designsystem.md.
 * Row min-height: 52px per designsystem.md.
 *
 * Sort: defaults to relevanceRank asc; user can toggle Confidence sort.
 *
 * Responsive breakpoints (wireframe SCR-014):
 *   1440px — all columns (Rank, Code, Description, Confidence, Justification, Status)
 *   768px  — Justification column hidden (sm: { display: 'table-cell' })
 *   375px  — Card layout rendered instead of table (via parent MedicalCodingReview)
 */

import { useMemo, useState } from 'react';
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
import CodeOffIcon from '@mui/icons-material/CodeOff';
import { visuallyHidden } from '@mui/utils';

import Icd10CodeRow from './Icd10CodeRow';
import UncodableAlert from './UncodableAlert';
import type { Icd10CodeDto } from '@/hooks/useIcd10Codes';

// ─── Types ────────────────────────────────────────────────────────────────────

type SortColumn   = 'relevanceRank' | 'confidenceScore';
type SortDirection = 'asc' | 'desc';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Icd10CodeTableProps {
  codes:      Icd10CodeDto[];
  isLoading:  boolean;
  isError:    boolean;
  onRetry:    () => void;
  /** Optional map from codeValue → payer status badge (US_051). Omit when validation not yet run. */
  payerStatusByCode?: Record<string, 'valid' | 'warning' | 'denial-risk'>;
}

// ─── Skeleton rows ────────────────────────────────────────────────────────────

function LoadingRows() {
  return (
    <>
      {Array.from({ length: 5 }).map((_, i) => (
        <TableRow key={i} sx={{ minHeight: 52 }}>
          <TableCell><Skeleton variant="text" width={24} /></TableCell>
          <TableCell><Skeleton variant="text" width={72} /></TableCell>
          <TableCell><Skeleton variant="text" width={200} /></TableCell>
          <TableCell><Skeleton variant="rounded" width={44} height={20} sx={{ borderRadius: '9999px' }} /></TableCell>
          <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>
            <Skeleton variant="text" width={180} />
          </TableCell>
          <TableCell><Skeleton variant="text" width={60} /></TableCell>
        </TableRow>
      ))}
    </>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function Icd10CodeTable({
  codes,
  isLoading,
  isError,
  onRetry,
  payerStatusByCode,
}: Icd10CodeTableProps) {
  const [sortColumn, setSortColumn]     = useState<SortColumn>('relevanceRank');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');

  function handleSort(col: SortColumn) {
    if (col === sortColumn) {
      setSortDirection(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      setSortColumn(col);
      setSortDirection('asc');
    }
  }

  const uncodableCount = useMemo(
    () => codes.filter(c => c.codeValue === 'UNCODABLE').length,
    [codes],
  );

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
        action={
          <Button color="inherit" size="small" onClick={onRetry}>
            Retry
          </Button>
        }
        sx={{ mb: 2 }}
      >
        Failed to load ICD-10 code suggestions. Check your connection and try again.
      </Alert>
    );
  }

  // ── Empty state (non-loading, no codes) ──────────────────────────────────
  if (!isLoading && codes.length === 0) {
    return (
      <Box
        sx={{
          display:        'flex',
          flexDirection:  'column',
          alignItems:     'center',
          py:             8,
          color:          'text.secondary',
          gap:            2,
        }}
        role="status"
        aria-label="No ICD-10 code suggestions available"
      >
        <CodeOffIcon sx={{ fontSize: 56, color: 'text.disabled' }} />
        <Typography variant="h6" color="text.secondary">
          No code suggestions yet
        </Typography>
        <Typography variant="body2" color="text.disabled" textAlign="center" maxWidth={360}>
          ICD-10 codes will appear here after the AI coding pipeline has processed this
          patient's clinical data.
        </Typography>
      </Box>
    );
  }

  // ── Validation state overlay (UNCODABLE items) ───────────────────────────
  const validationBanner = <UncodableAlert uncodableCount={uncodableCount} />;

  // ── Default + Loading states ──────────────────────────────────────────────
  return (
    <Box>
      {validationBanner}

      <TableContainer
        component={Paper}
        variant="outlined"
        aria-label="ICD-10 code suggestions table"
        sx={{ borderRadius: 2 }}
      >
        <Table size="medium" aria-describedby="icd10-table-desc">
          <caption id="icd10-table-desc" style={visuallyHidden}>
            AI-generated ICD-10 code suggestions sorted by {sortColumn === 'relevanceRank' ? 'relevance' : 'confidence'}, {sortDirection === 'asc' ? 'ascending' : 'descending'}.
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
                  ICD-10 Code
                </Typography>
              </TableCell>

              {/* Description */}
              <TableCell>
                <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                  Description
                </Typography>
              </TableCell>

              {/* Confidence */}
              <TableCell sx={{ width: 100 }}>
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

              {/* Justification (hidden at sm) */}
              <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, maxWidth: 320 }}>
                <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                  Justification
                </Typography>
              </TableCell>

              {/* Status */}
              <TableCell sx={{ width: 120 }}>
                <Typography variant="subtitle1" fontWeight={600} fontSize="0.8125rem">
                  Status
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
                <Icd10CodeRow
                  key={code.medicalCodeId ?? `${code.codeValue}-${idx}`}
                  code={code}
                  rank={idx + 1}
                  hideJustificationColumn={false}
                  payerStatus={payerStatusByCode?.[code.codeValue]}
                />
              ))
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
}
