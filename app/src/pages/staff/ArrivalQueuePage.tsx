/**
 * ArrivalQueuePage (staff/) — Real-Time Arrival Queue Dashboard (US_053/US_054/US_056, SCR-011).
 *
 * Composes:
 *   - Breadcrumb: Staff Dashboard > Arrival Queue
 *   - Page header: "Today's Queue" + live count (aria-live) + avg wait + last-updated timestamp (EC-2)
 *   - Manual Refresh button (EC-2)
 *   - QueueFilters card (provider + appointment type + status dropdowns + Clear All, US_056 AC-2)
 *   - 30-minute wait alert banner (when any waiting patient exceeds the threshold)
 *   - ArrivalQueueTable (sortable, drag-and-drop reorder, with action callbacks)
 *   - MUI Pagination (25 items / page — EC-1, client-side slice after filter)
 *   - Empty state: "No patients match" when all three filters return 0 results (US_056 AC-2)
 *   - Error Snackbar (API errors + 409 duplicate-arrival message)
 *   - ARIA live region for queue position change announcements (UXR-206)
 *   - "Queue History" tab with date-range analytics and CSV export (US_056 AC-3, AC-4)
 *
 * State:
 *   filters     — { provider, appointmentType, status, page }  (page resets to 1 on filter change)
 *   activeTab   — 'today' | 'history'
 *   sortColumn  — 'patientName' | 'appointmentTime' | 'waitTimeMinutes'
 *   sortDir     — 'asc' | 'desc'
 *   snackbar    — { open, message, severity }
 *
 * Auto-refresh: every 5 s via useQueueData (UXR-103).
 * Last updated: ISO timestamp formatted to HH:MM AM/PM from dataUpdatedAt (EC-2).
 */

import { useMemo, useRef, useState } from 'react';

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Container from '@mui/material/Container';
import MuiLink from '@mui/material/Link';
import Pagination from '@mui/material/Pagination';
import Skeleton from '@mui/material/Skeleton';
import Snackbar from '@mui/material/Snackbar';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import FilterListOffIcon from '@mui/icons-material/FilterListOff';
import RefreshIcon from '@mui/icons-material/Refresh';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { Link as RouterLink } from 'react-router-dom';

import QueueHistoryView from '@/components/queue/QueueHistoryView';

import ArrivalQueueTable, { type SortColumn, type SortDirection } from '@/components/queue/ArrivalQueueTable';
import AverageWaitTimeSummary from '@/components/queue/AverageWaitTimeSummary';
import QueueFilters from '@/components/queue/QueueFilters';
import { PAGE_SIZE, useQueueData } from '@/hooks/useQueueData';
import type { QueueEntry, QueueFilters as Filters } from '@/hooks/useQueueData';
import { useMarkArrived } from '@/hooks/useMarkArrived';
import { useUpdateQueueStatus } from '@/hooks/useUpdateQueueStatus';
import { useQueuePriority } from '@/hooks/useQueuePriority';
import { useQueueReorder, ReorderConflictError } from '@/hooks/useQueueReorder';
import { useWaitThreshold } from '@/hooks/useWaitThreshold';
import { useQueueStore, sortByPriorityTier } from '@/stores/queueStore';

// ─── Constants ────────────────────────────────────────────────────────────────

// Threshold is now fetched dynamically via useWaitThreshold (US_055 AC-3).

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatUpdatedAt(ts: number): string {
  return new Intl.DateTimeFormat(undefined, {
    hour: 'numeric', minute: '2-digit', hour12: true,
  }).format(new Date(ts));
}

function clientFilter(
  entries:         QueueEntry[],
  provider:        string,
  appointmentType: string,
  status:          string,
): QueueEntry[] {
  return entries.filter((e) => {
    const matchProvider        = provider        === 'all' || e.providerName    === provider;
    const matchAppointmentType = appointmentType === 'all' || e.appointmentType === appointmentType;
    const matchStatus          = status          === 'all' || e.status          === status;
    return matchProvider && matchAppointmentType && matchStatus;
  });
}

function clientSort(
  entries: QueueEntry[],
  col: SortColumn,
  dir: SortDirection,
): QueueEntry[] {
  // When sorting by appointment time (default), apply two-tier priority sort (US_054):
  // urgent patients first (by arrivalTimestamp), then normal (by arrivalTimestamp)
  if (col === 'appointmentTime') {
    return sortByPriorityTier(entries);
  }
  return [...entries].sort((a, b) => {
    let cmp = 0;
    if (col === 'patientName') {
      cmp = a.patientName.localeCompare(b.patientName);
    } else {
      cmp = a.waitTimeMinutes - b.waitTimeMinutes;
    }
    return dir === 'asc' ? cmp : -cmp;
  });
}

// ─── Snackbar state ───────────────────────────────────────────────────────────

interface SnackbarState {
  open:     boolean;
  message:  string;
  severity: 'success' | 'error' | 'warning' | 'info';
}

const SNACKBAR_CLOSED: SnackbarState = { open: false, message: '', severity: 'info' };

// ─── Component ────────────────────────────────────────────────────────────────

export default function ArrivalQueuePage() {
  // ── Filter / sort / tab state ─────────────────────────────────────────────
  const [filters, setFilters] = useState<Filters>({ provider: 'all', appointmentType: 'all', status: 'all', page: 1 });
  const [activeTab,   setActiveTab]   = useState<'today' | 'history'>('today');
  const [sortColumn,  setSortColumn]  = useState<SortColumn>('appointmentTime');
  const [sortDir,     setSortDir]     = useState<SortDirection>('asc');
  const [snackbar,    setSnackbar]    = useState<SnackbarState>(SNACKBAR_CLOSED);

  // ── ARIA live region ref for queue position change announcements (UXR-206) ──
  const ariaLiveRef = useRef<HTMLSpanElement>(null);

  // ── Data fetching ────────────────────────────────────────────────────────
  const { data, isLoading, isFetching, dataUpdatedAt, refetch } = useQueueData(filters);
  const allEntries: QueueEntry[] = data?.data ?? [];

  // ── Wait time threshold (configurable, US_055 AC-3) ─────────────────────
  const { thresholdMinutes } = useWaitThreshold();

  // ── Local order (optimistic reorder) from Zustand store ─────────────────
  const { localOrder, setLocalOrder, clearLocalOrder } = useQueueStore();

  // ── Client-side filter + sort + local-order apply + page slice ───────────
  const filtered = useMemo(
    () => clientFilter(allEntries, filters.provider, filters.appointmentType, filters.status),
    [allEntries, filters.provider, filters.appointmentType, filters.status],
  );
  const sorted = useMemo(() => {
    const serverSorted = clientSort(filtered, sortColumn, sortDir);
    if (!localOrder) return serverSorted;
    // Apply local ordering — entries not in localOrder fall to the end
    const orderMap = new Map(localOrder.map((id, i) => [id, i]));
    return [...serverSorted].sort((a, b) => {
      const ai = orderMap.get(a.queueId) ?? Number.MAX_SAFE_INTEGER;
      const bi = orderMap.get(b.queueId) ?? Number.MAX_SAFE_INTEGER;
      return ai - bi;
    });
  }, [filtered, sortColumn, sortDir, localOrder]);

  const totalPages   = Math.max(1, Math.ceil(sorted.length / PAGE_SIZE));
  const pageEntries  = useMemo(
    () => sorted.slice((filters.page - 1) * PAGE_SIZE, filters.page * PAGE_SIZE),
    [sorted, filters.page],
  );

  // ── Long-wait alert: count of patients over threshold (US_055 AC-2) ─────
  const longWaitCount = filtered.filter(
    (e) =>
      (e.status === 'waiting' || e.status === 'arrived_late') &&
      e.waitTimeMinutes >= thresholdMinutes,
  ).length;
  const hasLongWait = longWaitCount > 0;

  // ── Mutations ────────────────────────────────────────────────────────────
  const markArrived  = useMarkArrived();
  const updateStatus = useUpdateQueueStatus();
  const markPriority = useQueuePriority();
  const reorder      = useQueueReorder();
  const isMutating   = markArrived.isPending || updateStatus.isPending || markPriority.isPending;

  function handleMarkArrived(appointmentId: string) {
    markArrived.mutate(
      { appointmentId },
      {
        onError: (err) =>
          setSnackbar({
            open: true,
            message: err.isDuplicate ? err.message : 'Failed to mark patient as arrived.',
            severity: err.isDuplicate ? 'warning' : 'error',
          }),
      },
    );
  }

  function handleMarkInVisit(queueId: string) {
    updateStatus.mutate(
      { queueId, status: 'in_visit' },
      {
        onError: () =>
          setSnackbar({ open: true, message: 'Failed to update status.', severity: 'error' }),
      },
    );
  }

  function handleMarkUrgent(queueId: string) {
    markPriority.mutate(
      { queueId, priority: 'urgent' },
      {
        onError: () =>
          setSnackbar({ open: true, message: 'Failed to mark patient as urgent.', severity: 'error' }),
      },
    );
  }

  function handleReorder(fromQueueId: string, toQueueId: string) {
    // Build optimistic order — move fromQueueId to the position of toQueueId
    const currentIds = sorted.map((e) => e.queueId);
    const fromIdx    = currentIds.indexOf(fromQueueId);
    const toIdx      = currentIds.indexOf(toQueueId);
    if (fromIdx === -1 || toIdx === -1) return;

    const newIds = [...currentIds];
    newIds.splice(fromIdx, 1);
    newIds.splice(toIdx, 0, fromQueueId);
    setLocalOrder(newIds);

    // Announce to screen readers (UXR-206)
    const movedEntry = sorted.find((e) => e.queueId === fromQueueId);
    if (movedEntry && ariaLiveRef.current) {
      ariaLiveRef.current.textContent =
        `${movedEntry.patientName} moved to position ${toIdx + 1}.`;
    }

    // newPosition is 1-based index in the new order
    const newPosition = toIdx + 1;

    reorder.mutate(
      { queueId: fromQueueId, newPosition },
      {
        onSuccess: () => clearLocalOrder(),
        onError: (err) => {
          clearLocalOrder();
          if (err instanceof ReorderConflictError) {
            setSnackbar({ open: true, message: err.message, severity: 'info' });
          } else {
            setSnackbar({ open: true, message: 'Failed to reorder queue.', severity: 'error' });
          }
        },
      },
    );
  }

  // ── Sort toggle ───────────────────────────────────────────────────────────
  function handleSort(col: SortColumn) {
    if (col === sortColumn) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortColumn(col);
      setSortDir('asc');
    }
    setFilters((f) => ({ ...f, page: 1 }));
  }

  // ── Filter change helpers ─────────────────────────────────────────────────
  function handleProviderChange(value: string) {
    setFilters((f) => ({ ...f, provider: value, page: 1 }));
  }

  function handleAppointmentTypeChange(value: string) {
    setFilters((f) => ({ ...f, appointmentType: value, page: 1 }));
  }

  function handleStatusChange(value: string) {
    setFilters((f) => ({ ...f, status: value, page: 1 }));
  }

  function handleClearAllFilters() {
    setFilters((f) => ({ ...f, provider: 'all', appointmentType: 'all', status: 'all', page: 1 }));
  }

  function handlePageChange(_: React.ChangeEvent<unknown>, page: number) {
    setFilters((f) => ({ ...f, page }));
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <Container maxWidth="xl" sx={{ py: 3 }}>
      {/* ARIA live region for queue position change announcements (UXR-206) */}
      <span
        ref={ariaLiveRef}
        aria-live="assertive"
        aria-atomic="true"
        style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap' }}
      />

      {/* Breadcrumb */}
      <Breadcrumbs aria-label="breadcrumb" sx={{ mb: 2 }}>
        <MuiLink component={RouterLink} to="/staff/dashboard" underline="hover" color="inherit">
          Staff Dashboard
        </MuiLink>
        <Typography color="text.primary">Arrival Queue</Typography>
      </Breadcrumbs>

      {/* Page header */}
      <Box
        sx={{
          display:        'flex',
          alignItems:     'center',
          flexWrap:       'wrap',
          gap:            2,
          mb:             2,
        }}
      >
        <Typography variant="h5" component="h1" fontWeight={700}>
          Today's Queue
        </Typography>

        {/* Live queue count with aria-live for screen reader updates (UXR-206) */}
        <Typography
          variant="body2"
          color="text.secondary"
          aria-live="polite"
          aria-atomic="true"
        >
          {isLoading ? (
            <Skeleton width={80} component="span" />
          ) : (
            `${sorted.length} patient${sorted.length !== 1 ? 's' : ''}`
          )}
        </Typography>

        {/* Average wait summary */}
        {!isLoading && <AverageWaitTimeSummary entries={filtered} />}

        {/* Spacer */}
        <Box sx={{ flex: 1 }} />

        {/* Last updated + refresh button (EC-2) */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="caption" color="text.secondary">
            {dataUpdatedAt ? `Last updated: ${formatUpdatedAt(dataUpdatedAt)}` : ''}
          </Typography>
          <Button
            variant="outlined"
            size="small"
            startIcon={isFetching ? <CircularProgress size={14} /> : <RefreshIcon fontSize="small" />}
            aria-label="Refresh queue"
            disabled={isFetching}
            onClick={() => void refetch()}
          >
            Refresh
          </Button>
        </Box>
      </Box>

      {/* Tab navigation: Today's Queue / Queue History (US_056 AC-3) */}
      <Tabs
        value={activeTab}
        onChange={(_, v: 'today' | 'history') => setActiveTab(v)}
        sx={{ mb: 2 }}
        aria-label="Queue views"
      >
        <Tab value="today"   label="Today's Queue" id="tab-today"   aria-controls="tabpanel-today"   />
        <Tab value="history" label="Queue History"  id="tab-history" aria-controls="tabpanel-history" />
      </Tabs>

      {/* ── History tab panel ──────────────────────────────────────────────── */}
      {activeTab === 'history' && (
        <div role="tabpanel" id="tabpanel-history" aria-labelledby="tab-history">
          <QueueHistoryView
            onExportError={(msg) => setSnackbar({ open: true, message: msg, severity: 'error' })}
          />
        </div>
      )}

      {/* ── Today tab panel ────────────────────────────────────────────────── */}
      {activeTab === 'today' && (
        <div role="tabpanel" id="tabpanel-today" aria-labelledby="tab-today">
          {/* Filters (US_056 AC-1, AC-2) */}
          <QueueFilters
            entries={allEntries}
            selectedProvider={filters.provider}
            selectedAppointmentType={filters.appointmentType}
            selectedStatus={filters.status}
            onProviderChange={handleProviderChange}
            onAppointmentTypeChange={handleAppointmentTypeChange}
            onStatusChange={handleStatusChange}
            onClearAll={handleClearAllFilters}
          />

          {/* Wait time threshold-exceeded alert banner (US_055 AC-2, UXR-206) */}
          {hasLongWait && (
            <Alert
              severity="warning"
              icon={<WarningAmberIcon />}
              sx={{ mb: 2 }}
              role="alert"
              aria-live="polite"
            >
              ⚠ {longWaitCount} patient{longWaitCount !== 1 ? 's' : ''} waiting over {thresholdMinutes} minute{thresholdMinutes !== 1 ? 's' : ''}.
            </Alert>
          )}

          {/* Empty state — no patients match the active filters (US_056 AC-2 edge case) */}
          {!isLoading && sorted.length === 0 && (
            <Box
              sx={{
                textAlign: 'center',
                py: 6,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 1.5,
              }}
              role="status"
              aria-live="polite"
            >
              <FilterListOffIcon sx={{ fontSize: 48, color: 'text.disabled' }} />
              <Typography variant="h6" color="text.secondary">
                No patients match the selected filters
              </Typography>
              <Button
                variant="outlined"
                size="small"
                onClick={handleClearAllFilters}
                aria-label="Clear all queue filters"
              >
                Clear All Filters
              </Button>
            </Box>
          )}

          {/* Queue table */}
          {sorted.length > 0 && (
            <ArrivalQueueTable
              entries={pageEntries}
              isLoading={isLoading}
              sortColumn={sortColumn}
              sortDirection={sortDir}
              onSort={handleSort}
              onMarkInVisit={handleMarkInVisit}
              onMarkArrived={handleMarkArrived}
              onMarkUrgent={handleMarkUrgent}
              onReorder={handleReorder}
              isReordering={reorder.isPending}
              isMutating={isMutating}
              thresholdMinutes={thresholdMinutes}
            />
          )}

          {/* Pagination */}
          {totalPages > 1 && (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
              <Pagination
                count={totalPages}
                page={filters.page}
                onChange={handlePageChange}
                color="primary"
                aria-label="Queue page navigation"
              />
            </Box>
          )}
        </div>
      )}

      {/* Error / info snackbar — page-level, outside tab panels */}
      <Snackbar
        open={snackbar.open}
        autoHideDuration={6_000}
        onClose={() => setSnackbar(SNACKBAR_CLOSED)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          onClose={() => setSnackbar(SNACKBAR_CLOSED)}
          severity={snackbar.severity}
          variant="filled"
          sx={{ width: '100%' }}
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Container>
  );
}
