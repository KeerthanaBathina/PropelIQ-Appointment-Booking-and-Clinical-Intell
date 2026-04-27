/**
 * LatencyMetricsTable — MUI Table showing P50/P95 latencies per AI operation type (US_072 AC-3).
 *
 * Rows: Intake (<1 s), Document Parsing (<30 s), Medical Coding (<5 s)
 * Status column: green "Within Target" Chip when P95 ≤ target, red "Above Target" otherwise.
 * Formats ms values as "X ms" or "X.X s" depending on magnitude.
 * Shows "No data" row when sampleSize = 0 (US_067 dependency not yet satisfied).
 */

import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import type { OperationLatency } from '../types';

// ─── Props ────────────────────────────────────────────────────────────────────

interface LatencyMetricsTableProps {
  latencies: OperationLatency[];
  isLoading?: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

const OPERATION_LABELS: Record<string, string> = {
  Intake:          'Intake',
  DocumentParsing: 'Document Parsing',
  MedicalCoding:   'Medical Coding',
};

function formatMs(ms: number): string {
  if (ms === 0) return '—';
  if (ms >= 1_000) return `${(ms / 1_000).toFixed(1)} s`;
  return `${Math.round(ms)} ms`;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function LatencyMetricsTable({
  latencies,
  isLoading = false,
}: LatencyMetricsTableProps) {
  return (
    <TableContainer
      component={Paper}
      variant="outlined"
      sx={{ borderRadius: 2 }}
      aria-label="AI latency metrics"
    >
      <Table size="small">
        <TableHead>
          <TableRow sx={{ bgcolor: 'grey.50' }}>
            <TableCell sx={{ fontWeight: 600 }}>Operation</TableCell>
            <TableCell align="right" sx={{ fontWeight: 600 }}>P50</TableCell>
            <TableCell align="right" sx={{ fontWeight: 600 }}>P95</TableCell>
            <TableCell align="right" sx={{ fontWeight: 600 }}>Target P95</TableCell>
            <TableCell align="center" sx={{ fontWeight: 600 }}>Status</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {isLoading
            ? Array.from({ length: 3 }).map((_, i) => (
                <TableRow key={i}>
                  {Array.from({ length: 5 }).map((__, j) => (
                    <TableCell key={j}>
                      <Skeleton variant="text" />
                    </TableCell>
                  ))}
                </TableRow>
              ))
            : latencies.map((row) => {
                const hasData   = row.sampleSize > 0;
                const isOk      = hasData && row.p95Milliseconds <= row.targetP95Milliseconds;
                const label     = OPERATION_LABELS[row.operationType] ?? row.operationType;

                return (
                  <TableRow key={row.operationType} hover>
                    <TableCell>
                      <Typography variant="body2" fontWeight={500}>{label}</Typography>
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="body2" color="text.secondary">
                        {hasData ? formatMs(row.p50Milliseconds) : '—'}
                      </Typography>
                    </TableCell>
                    <TableCell align="right">
                      <Typography
                        variant="body2"
                        fontWeight={500}
                        sx={{ color: hasData ? (isOk ? 'success.main' : 'error.main') : 'text.disabled' }}
                      >
                        {hasData ? formatMs(row.p95Milliseconds) : '—'}
                      </Typography>
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="body2" color="text.secondary">
                        {formatMs(row.targetP95Milliseconds)}
                      </Typography>
                    </TableCell>
                    <TableCell align="center">
                      {!hasData ? (
                        <Chip
                          label="No data"
                          size="small"
                          sx={{ bgcolor: 'grey.100', color: 'text.secondary', fontSize: '0.7rem' }}
                        />
                      ) : isOk ? (
                        <Chip
                          label="Within Target"
                          size="small"
                          color="success"
                          variant="outlined"
                          sx={{ fontSize: '0.7rem' }}
                        />
                      ) : (
                        <Chip
                          label="Above Target"
                          size="small"
                          color="error"
                          variant="outlined"
                          sx={{ fontSize: '0.7rem' }}
                        />
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
