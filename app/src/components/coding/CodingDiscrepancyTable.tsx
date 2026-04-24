/**
 * CodingDiscrepancyTable — MUI Table listing AI vs staff coding discrepancies.
 * (US_050, AC-3, FR-068, SCR-014)
 *
 * Columns (all sortable):
 *   Code (AI Suggested) | Staff Selected | Code Type | Discrepancy Type |
 *   Justification (truncated, full text in Tooltip) | Detected Date
 *
 * States (all 5 required per UXR-502, UXR-601):
 *   Loading  — Skeleton rows
 *   Error    — Alert with retry button
 *   Empty    — "No discrepancies found" message
 *   Default  — Data table
 *
 * Discrepancy type chips:
 *   FullOverride    → error chip   (red)
 *   PartialOverride → warning chip (amber), partial overrides count as disagreement per EC-1
 *   MultipleCodes   → default chip (grey)
 *
 * Pagination: 10 rows per page.
 * Responsive: horizontal scroll on narrow viewports.
 * Keyboard navigation: all interactive elements have ARIA labels (UXR-202, UXR-203).
 */

import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import type { CodingDiscrepancyDto } from '@/hooks/useAgreementRate';

// ─── Types ────────────────────────────────────────────────────────────────────

type SortField = 'ai_suggested_code' | 'staff_selected_code' | 'code_type' | 'discrepancy_type' | 'detected_at';
type SortDir   = 'asc' | 'desc';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function discrepancyChipColor(type: string): 'error' | 'warning' | 'default' {
  if (type === 'FullOverride')    return 'error';
  if (type === 'PartialOverride') return 'warning';
  return 'default';
}

function discrepancyLabel(type: string): string {
  if (type === 'FullOverride')    return 'Full Override';
  if (type === 'PartialOverride') return 'Partial Override';
  if (type === 'MultipleCodes')   return 'Multiple Codes';
  return type;
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString(undefined, {
      year:  'numeric',
      month: 'short',
      day:   'numeric',
    });
  } catch {
    return iso;
  }
}

function sortRows(
  rows:  CodingDiscrepancyDto[],
  field: SortField,
  dir:   SortDir,
): CodingDiscrepancyDto[] {
  return [...rows].sort((a, b) => {
    const av = a[field] ?? '';
    const bv = b[field] ?? '';
    const cmp = av < bv ? -1 : av > bv ? 1 : 0;
    return dir === 'asc' ? cmp : -cmp;
  });
}

const PAGE_SIZE = 10;

// ─── Props ────────────────────────────────────────────────────────────────────

interface CodingDiscrepancyTableProps {
  data:      CodingDiscrepancyDto[];
  isLoading: boolean;
  isError:   boolean;
  onRetry:   () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CodingDiscrepancyTable({
  data,
  isLoading,
  isError,
  onRetry,
}: CodingDiscrepancyTableProps) {
  const [sortField, setSortField] = useState<SortField>('detected_at');
  const [sortDir,   setSortDir]   = useState<SortDir>('desc');
  const [page,      setPage]      = useState(0);

  // ── Loading state ────────────────────────────────────────────────────────
  if (isLoading) {
    return (
      <Box aria-busy="true" aria-label="Loading discrepancy data">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} variant="rounded" height={44} sx={{ mb: 1, borderRadius: 1 }} />
        ))}
      </Box>
    );
  }

  // ── Error state ──────────────────────────────────────────────────────────
  if (isError) {
    return (
      <Alert
        severity="error"
        role="alert"
        action={
          <Button color="inherit" size="small" onClick={onRetry} aria-label="Retry loading discrepancies">
            Retry
          </Button>
        }
      >
        Failed to load discrepancy records. Please retry.
      </Alert>
    );
  }

  // ── Empty state ──────────────────────────────────────────────────────────
  if (data.length === 0) {
    return (
      <Box
        sx={{
          py:         4,
          textAlign:  'center',
          color:      'text.secondary',
          bgcolor:    'grey.50',
          borderRadius: 2,
          border:     '1px solid',
          borderColor: 'divider',
        }}
        role="status"
      >
        <Typography variant="body2">No discrepancies found for the selected period.</Typography>
      </Box>
    );
  }

  // ── Sort + paginate ──────────────────────────────────────────────────────
  const sorted = sortRows(data, sortField, sortDir);
  const slice  = sorted.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE);

  function handleSort(field: SortField) {
    if (field === sortField) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      setSortField(field);
      setSortDir('asc');
    }
    setPage(0);
  }

  // ── Table ────────────────────────────────────────────────────────────────
  return (
    <Box>
      <TableContainer
        sx={{
          overflowX: 'auto',
          border:    '1px solid',
          borderColor: 'divider',
          borderRadius: 2,
        }}
      >
        <Table
          size="small"
          aria-label="Coding discrepancy table"
          sx={{ minWidth: 720 }}
        >
          <TableHead>
            <TableRow sx={{ bgcolor: 'grey.50' }}>
              {/* AI Suggested */}
              <TableCell>
                <TableSortLabel
                  active={sortField === 'ai_suggested_code'}
                  direction={sortField === 'ai_suggested_code' ? sortDir : 'asc'}
                  onClick={() => handleSort('ai_suggested_code')}
                  aria-label="Sort by AI suggested code"
                >
                  <Typography variant="caption" fontWeight={600}>AI Suggested</Typography>
                </TableSortLabel>
              </TableCell>

              {/* Staff Selected */}
              <TableCell>
                <TableSortLabel
                  active={sortField === 'staff_selected_code'}
                  direction={sortField === 'staff_selected_code' ? sortDir : 'asc'}
                  onClick={() => handleSort('staff_selected_code')}
                  aria-label="Sort by staff selected code"
                >
                  <Typography variant="caption" fontWeight={600}>Staff Selected</Typography>
                </TableSortLabel>
              </TableCell>

              {/* Code Type */}
              <TableCell>
                <TableSortLabel
                  active={sortField === 'code_type'}
                  direction={sortField === 'code_type' ? sortDir : 'asc'}
                  onClick={() => handleSort('code_type')}
                  aria-label="Sort by code type"
                >
                  <Typography variant="caption" fontWeight={600}>Code Type</Typography>
                </TableSortLabel>
              </TableCell>

              {/* Discrepancy Type */}
              <TableCell>
                <TableSortLabel
                  active={sortField === 'discrepancy_type'}
                  direction={sortField === 'discrepancy_type' ? sortDir : 'asc'}
                  onClick={() => handleSort('discrepancy_type')}
                  aria-label="Sort by discrepancy type"
                >
                  <Typography variant="caption" fontWeight={600}>Discrepancy Type</Typography>
                </TableSortLabel>
              </TableCell>

              {/* Justification */}
              <TableCell>
                <Typography variant="caption" fontWeight={600}>Justification</Typography>
              </TableCell>

              {/* Detected Date */}
              <TableCell>
                <TableSortLabel
                  active={sortField === 'detected_at'}
                  direction={sortField === 'detected_at' ? sortDir : 'asc'}
                  onClick={() => handleSort('detected_at')}
                  aria-label="Sort by detected date"
                >
                  <Typography variant="caption" fontWeight={600}>Detected</Typography>
                </TableSortLabel>
              </TableCell>
            </TableRow>
          </TableHead>

          <TableBody>
            {slice.map(row => (
              <TableRow
                key={row.discrepancy_id}
                hover
                sx={{ '&:last-child td': { borderBottom: 0 } }}
              >
                {/* AI Suggested */}
                <TableCell>
                  <Typography
                    variant="body2"
                    component="code"
                    fontWeight={600}
                    sx={{ fontFamily: 'monospace' }}
                  >
                    {row.ai_suggested_code}
                  </Typography>
                </TableCell>

                {/* Staff Selected */}
                <TableCell>
                  <Typography
                    variant="body2"
                    component="code"
                    fontWeight={600}
                    sx={{ fontFamily: 'monospace' }}
                  >
                    {row.staff_selected_code}
                  </Typography>
                </TableCell>

                {/* Code Type */}
                <TableCell>
                  <Chip
                    label={row.code_type}
                    size="small"
                    variant="outlined"
                    sx={{ fontSize: '0.7rem', height: 20 }}
                    aria-label={`Code type: ${row.code_type}`}
                  />
                </TableCell>

                {/* Discrepancy Type */}
                <TableCell>
                  <Chip
                    label={discrepancyLabel(row.discrepancy_type)}
                    size="small"
                    color={discrepancyChipColor(row.discrepancy_type)}
                    variant="outlined"
                    sx={{ fontSize: '0.7rem', height: 20 }}
                    aria-label={`Discrepancy type: ${discrepancyLabel(row.discrepancy_type)}`}
                  />
                </TableCell>

                {/* Justification — truncated with Tooltip */}
                <TableCell sx={{ maxWidth: 200 }}>
                  {row.override_justification ? (
                    <Tooltip
                      title={row.override_justification}
                      placement="top"
                      arrow
                    >
                      <Typography
                        variant="caption"
                        sx={{
                          display:         '-webkit-box',
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: 'vertical',
                          overflow:        'hidden',
                          cursor:          'help',
                        }}
                        tabIndex={0}
                        aria-label={`Justification: ${row.override_justification}`}
                      >
                        {row.override_justification}
                      </Typography>
                    </Tooltip>
                  ) : (
                    <Typography variant="caption" color="text.disabled">—</Typography>
                  )}
                </TableCell>

                {/* Detected Date */}
                <TableCell>
                  <Typography variant="caption" color="text.secondary" noWrap>
                    {formatDate(row.detected_at)}
                  </Typography>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      {/* Pagination */}
      <TablePagination
        component="div"
        count={data.length}
        page={page}
        rowsPerPage={PAGE_SIZE}
        rowsPerPageOptions={[PAGE_SIZE]}
        onPageChange={(_, p) => setPage(p)}
        aria-label="Discrepancy table pagination"
      />
    </Box>
  );
}
