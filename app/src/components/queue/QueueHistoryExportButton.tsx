/**
 * QueueHistoryExportButton — CSV export trigger for queue history (US_056 AC-4).
 *
 * Calls GET /api/queue/history/export?startDate=&endDate= which returns a CSV blob.
 * Uses the browser-native Blob + URL.createObjectURL + <a> click pattern so no
 * additional dependencies are needed.
 *
 * The button is disabled when either date is missing or start > end, matching the
 * same guard applied by useQueueHistory.
 *
 * Accessibility: button aria-label includes the selected date range for screen readers.
 */

import { useState } from 'react';

import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import DownloadIcon from '@mui/icons-material/Download';

import { apiGetBlob } from '@/lib/apiClient';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  startDate: string;   // YYYY-MM-DD
  endDate:   string;   // YYYY-MM-DD
  /** Called when the export fails so the parent can show a snackbar. */
  onError?: (message: string) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function QueueHistoryExportButton({ startDate, endDate, onError }: Props) {
  const [isExporting, setIsExporting] = useState(false);

  const disabled =
    !startDate || !endDate || startDate > endDate || isExporting;

  async function handleExport() {
    setIsExporting(true);
    try {
      const params = new URLSearchParams({ startDate, endDate });
      const blob   = await apiGetBlob(`/api/queue/history/export?${params.toString()}`);

      // Derive a filename: queue-history-YYYYMMDD-YYYYMMDD.csv
      const name = `queue-history-${startDate.replace(/-/g, '')}-${endDate.replace(/-/g, '')}.csv`;

      // Browser-native download — no extra libraries needed
      const url  = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href     = url;
      link.download = name;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch {
      onError?.('Failed to export queue history. Please try again.');
    } finally {
      setIsExporting(false);
    }
  }

  return (
    <Button
      variant="outlined"
      size="small"
      startIcon={isExporting ? <CircularProgress size={14} /> : <DownloadIcon fontSize="small" />}
      disabled={disabled}
      onClick={() => void handleExport()}
      aria-label={
        startDate && endDate
          ? `Export queue history from ${startDate} to ${endDate} as CSV`
          : 'Export queue history as CSV'
      }
    >
      Export CSV
    </Button>
  );
}
