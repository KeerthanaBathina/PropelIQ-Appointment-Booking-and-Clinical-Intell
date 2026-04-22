/**
 * DataPointTable — US_043 SCR-013 clinical data table (wireframe data-table).
 *
 * Scrollable MUI Table for a given clinical data category with columns adapted
 * per dataType. Row click opens SourceCitationPanel (AC-3).
 * Flagged rows (flaggedForReview=true) get warning-surface background.
 * Confidence badge uses profile-specific thresholds (UXR-105).
 *
 * Keyboard accessible: rows focusable via Tab; Enter/Space trigger row click (UXR-202).
 */

import { useCallback } from 'react';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import ConfidenceBadge from './ConfidenceBadge';
import type { ProfileDataPointDto } from '@/hooks/usePatientProfile';

// ─── Column definitions ───────────────────────────────────────────────────────

interface Column {
  header: string;
  width?: number | string;
  render: (row: ProfileDataPointDto) => React.ReactNode;
}

function statusChip(status: ProfileDataPointDto['verificationStatus']): React.ReactNode {
  switch (status) {
    case 'Verified':  return <Chip label="Verified" color="success" size="small" />;
    case 'Corrected': return <Chip label="Corrected" color="info" size="small" />;
    default:          return <Chip label="Pending" variant="outlined" size="small" />;
  }
}

function sourceCell(row: ProfileDataPointDto): React.ReactNode {
  return (
    <Tooltip title={row.sourceAttribution ?? row.sourceDocumentName} arrow>
      <Typography variant="body2" noWrap sx={{ maxWidth: 140, cursor: 'default' }}>
        {row.sourceDocumentName}
      </Typography>
    </Tooltip>
  );
}

const MEDICATION_COLUMNS: Column[] = [
  { header: 'Medication', render: (r) => r.normalizedValue },
  { header: 'Details',    render: (r) => r.unit ? `${r.rawText} (${r.unit})` : r.rawText, width: 160 },
  { header: 'Source',     render: sourceCell },
  { header: 'Confidence', width: 100, render: (r) => <ConfidenceBadge score={r.confidenceScore} /> },
  { header: 'Status',     width: 100, render: (r) => statusChip(r.verificationStatus) },
];

const DIAGNOSIS_COLUMNS: Column[] = [
  { header: 'Code',        width: 80,  render: (r) => r.unit ?? '—' },
  { header: 'Description', render: (r) => r.normalizedValue },
  { header: 'Source',      render: sourceCell },
  { header: 'Confidence',  width: 100, render: (r) => <ConfidenceBadge score={r.confidenceScore} /> },
  { header: 'Status',      width: 100, render: (r) => statusChip(r.verificationStatus) },
];

const PROCEDURE_COLUMNS: Column[] = [
  { header: 'Code',        width: 80,  render: (r) => r.unit ?? '—' },
  { header: 'Description', render: (r) => r.normalizedValue },
  { header: 'Source',      render: sourceCell },
  { header: 'Confidence',  width: 100, render: (r) => <ConfidenceBadge score={r.confidenceScore} /> },
  { header: 'Status',      width: 100, render: (r) => statusChip(r.verificationStatus) },
];

const ALLERGY_COLUMNS: Column[] = [
  { header: 'Allergen',   render: (r) => r.normalizedValue },
  { header: 'Reaction',   render: (r) => r.rawText, width: 180 },
  { header: 'Source',     render: sourceCell },
  { header: 'Confidence', width: 100, render: (r) => <ConfidenceBadge score={r.confidenceScore} /> },
];

function getColumns(dataType: ProfileDataPointDto['dataType']): Column[] {
  switch (dataType) {
    case 'Medication': return MEDICATION_COLUMNS;
    case 'Diagnosis':  return DIAGNOSIS_COLUMNS;
    case 'Procedure':  return PROCEDURE_COLUMNS;
    case 'Allergy':    return ALLERGY_COLUMNS;
  }
}

// ─── Loading skeleton ─────────────────────────────────────────────────────────

function DataTableSkeleton({ columnCount }: { columnCount: number }) {
  return (
    <TableBody>
      {[1, 2, 3, 4].map((i) => (
        <TableRow key={i}>
          {Array.from({ length: columnCount }, (_, j) => (
            <TableCell key={j}>
              <Skeleton variant="text" />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </TableBody>
  );
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface DataPointTableProps {
  dataType: ProfileDataPointDto['dataType'];
  rows: ProfileDataPointDto[];
  isLoading?: boolean;
  onRowClick: (extractedDataId: string) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function DataPointTable({
  dataType,
  rows,
  isLoading = false,
  onRowClick,
}: DataPointTableProps) {
  const columns = getColumns(dataType);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent, id: string) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onRowClick(id);
      }
    },
    [onRowClick],
  );

  return (
    <TableContainer sx={{ overflowX: 'auto' }}>
      <Table size="small" aria-label={`${dataType} list`}>
        <TableHead>
          <TableRow>
            {columns.map((col) => (
              <TableCell
                key={col.header}
                sx={{ fontWeight: 600, whiteSpace: 'nowrap', width: col.width }}
              >
                {col.header}
              </TableCell>
            ))}
          </TableRow>
        </TableHead>

        {isLoading ? (
          <DataTableSkeleton columnCount={columns.length} />
        ) : (
          <TableBody>
            {rows.map((row) => (
              <TableRow
                key={row.extractedDataId}
                hover
                tabIndex={0}
                onClick={() => onRowClick(row.extractedDataId)}
                onKeyDown={(e) => handleKeyDown(e, row.extractedDataId)}
                aria-label={`View source citation for ${row.normalizedValue}`}
                sx={{
                  cursor: 'pointer',
                  bgcolor: row.flaggedForReview ? 'warning.surface' : undefined,
                  '&:hover': { bgcolor: row.flaggedForReview ? 'warning.light' : undefined },
                }}
              >
                {columns.map((col, i) => (
                  <TableCell key={i} sx={{ fontSize: '0.875rem' }}>
                    {i === 0 && row.flaggedForReview ? (
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                        <WarningAmberIcon
                          fontSize="small"
                          color="warning"
                          aria-hidden="true"
                          sx={{ flexShrink: 0 }}
                        />
                        {col.render(row)}
                      </Box>
                    ) : (
                      col.render(row)
                    )}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        )}
      </Table>
    </TableContainer>
  );
}
