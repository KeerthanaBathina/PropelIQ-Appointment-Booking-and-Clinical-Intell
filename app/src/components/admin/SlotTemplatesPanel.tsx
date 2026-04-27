/**
 * SlotTemplatesPanel — Weekly availability grid for SCR-015 Config tab (US_059 AC-1, AC-2).
 *
 * Wireframe: 7-day × 5-timeslot grid (extended from wireframe Mon–Fri to Mon–Sun).
 * Green cells = available (success.50), strikethrough = blocked (grey.100).
 * Provider selector dropdown fetches templates via GET /api/admin/config/slots/{providerId}.
 * "Save Template" PUTs updated blocks per day via /api/admin/config/slots/{providerId}/{day}.
 *
 * UXR-004: Auto-save on cell toggle (per-day PUT after 1500ms debounce).
 * UXR-102: Conflict dialog when API returns 409 (stale version).
 * UXR-502: Skeleton loading while fetching provider or template data.
 * UXR-501: Validation feedback within 200ms (handled by API + immediate local state).
 * Accessibility: role="grid", aria-pressed on each cell, keyboard Enter/Space toggles.
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  useSlotTemplatesByProvider,
  useUpsertSlotTemplate,
} from '@/hooks/useAdminConfig';
import { useAdminUsers } from '@/hooks/useAdminUsers';
import { useToast } from '@/components/common/ToastProvider';
import { ApiError } from '@/lib/apiClient';
import type { SlotTemplateBlockDto, SlotTemplateResponse } from '@/types/adminConfig';

// ─── Grid constants ───────────────────────────────────────────────────────────

// 0=Sun … 6=Sat. Columns show Mon→Sun to match wireframe + 7-day requirement.
const COLUMN_DAYS: Array<{ label: string; dayOfWeek: number }> = [
  { label: 'Mon', dayOfWeek: 1 },
  { label: 'Tue', dayOfWeek: 2 },
  { label: 'Wed', dayOfWeek: 3 },
  { label: 'Thu', dayOfWeek: 4 },
  { label: 'Fri', dayOfWeek: 5 },
  { label: 'Sat', dayOfWeek: 6 },
  { label: 'Sun', dayOfWeek: 0 },
];

// Default fixed time slots (mirrors wireframe). Each maps to an API startTime.
const TIME_ROWS: Array<{ label: string; apiTime: string }> = [
  { label: '9:00 AM',  apiTime: '09:00:00' },
  { label: '10:00 AM', apiTime: '10:00:00' },
  { label: '11:00 AM', apiTime: '11:00:00' },
  { label: '2:00 PM',  apiTime: '14:00:00' },
  { label: '3:00 PM',  apiTime: '15:00:00' },
];

// ─── Types ────────────────────────────────────────────────────────────────────

type CellKey  = `${number}:${string}`;  // "1:09:00:00"
type CellMap  = Record<CellKey, boolean>;
type VersionMap = Record<number, number | undefined>;

// ─── Helpers ─────────────────────────────────────────────────────────────────

function cellKey(dayOfWeek: number, apiTime: string): CellKey {
  return `${dayOfWeek}:${apiTime}`;
}

function buildCells(templates: SlotTemplateResponse[]): CellMap {
  const map: CellMap = {};
  for (const col of COLUMN_DAYS) {
    for (const row of TIME_ROWS) {
      map[cellKey(col.dayOfWeek, row.apiTime)] = true;
    }
  }
  for (const tmpl of templates) {
    for (const block of tmpl.blocks) {
      const key = cellKey(tmpl.dayOfWeek, block.startTime);
      if (key in map) map[key] = block.isAvailable;
    }
  }
  return map;
}

function buildVersionMap(templates: SlotTemplateResponse[]): VersionMap {
  const m: VersionMap = {};
  for (const t of templates) m[t.dayOfWeek] = t.version;
  return m;
}

function buildBlocks(cells: CellMap, dayOfWeek: number): SlotTemplateBlockDto[] {
  return TIME_ROWS.map(row => ({
    startTime:       row.apiTime,
    endTime:         row.apiTime.replace(/^(\d{2})/, v => String(parseInt(v, 10) + 1).padStart(2, '0')),
    appointmentType: 'General',
    isAvailable:     cells[cellKey(dayOfWeek, row.apiTime)] ?? true,
  }));
}


// ─── Component ────────────────────────────────────────────────────────────────

export default function SlotTemplatesPanel() {
  const { showToast } = useToast();

  // ── Provider list (Admin Users API — filter to "Provider" role subtitle)
  const { data: usersData, isLoading: usersLoading } = useAdminUsers();
  const providers = (usersData?.users ?? []).filter(
    u => u.roleSubtitle?.toLowerCase().includes('provider'),
  );

  const [selectedProviderId, setSelectedProviderId] = useState<string>('');

  useEffect(() => {
    if (!selectedProviderId && providers.length > 0) {
      setSelectedProviderId(providers[0].id);
    }
  }, [providers, selectedProviderId]);

  // ── Slot templates for selected provider
  const { data: templates, isLoading: tmplLoading } =
    useSlotTemplatesByProvider(selectedProviderId || null);

  const { mutateAsync: upsert, isLoading: isSaving } = useUpsertSlotTemplate();

  // ── Local state
  const [cells, setCells]       = useState<CellMap>({});
  const [versions, setVersions] = useState<VersionMap>({});
  const changedDaysRef = useRef<Set<number>>(new Set());
  const debounceRef    = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Conflict dialog
  const [conflictOpen, setConflictOpen] = useState(false);
  const [conflictDay,  setConflictDay]  = useState<number | null>(null);

  useEffect(() => {
    if (templates) {
      setCells(buildCells(templates));
      setVersions(buildVersionMap(templates));
      changedDaysRef.current.clear();
    }
  }, [templates, selectedProviderId]);

  // ── Auto-save: flush changed days after 1500ms idle (UXR-004)
  const scheduleSave = useCallback(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      void flushSave(changedDaysRef.current);
    }, 1500);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function flushSave(days: Set<number>) {
    if (!selectedProviderId || days.size === 0) return;
    const snapshot = new Set(days);
    days.clear();

    let saved = false;
    for (const dayOfWeek of snapshot) {
      try {
        const result = await upsert({
          providerId: selectedProviderId,
          dayOfWeek,
          body: { version: versions[dayOfWeek], blocks: buildBlocks(cells, dayOfWeek) },
        });
        setVersions(prev => ({ ...prev, [dayOfWeek]: result.version }));
        saved = true;
      } catch (err) {
        if (err instanceof ApiError && err.status === 409) {
          setConflictDay(dayOfWeek);
          setConflictOpen(true);
        } else {
          showToast({ message: 'Failed to save template. Please retry.', severity: 'error' });
        }
      }
    }
    if (saved) showToast({ message: 'Slot template saved.', severity: 'success' });
  }

  function toggleCell(dayOfWeek: number, apiTime: string) {
    const key = cellKey(dayOfWeek, apiTime);
    setCells(prev => ({ ...prev, [key]: !prev[key] }));
    changedDaysRef.current.add(dayOfWeek);
    scheduleSave();
  }

  async function handleSaveAll() {
    const allDays = new Set(COLUMN_DAYS.map(c => c.dayOfWeek));
    changedDaysRef.current = allDays;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    await flushSave(allDays);
  }

  const isLoading = usersLoading || (!!selectedProviderId && tmplLoading);

  if (isLoading) {
    return (
      <Box>
        <Skeleton variant="rectangular" height={32} width={200} sx={{ mb: 2 }} />
        <Skeleton variant="rectangular" height={220} />
      </Box>
    );
  }

  const activeProvider = providers.find(p => p.id === selectedProviderId);

  return (
    <Box>
      {/* ── Header row ── */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2, flexWrap: 'wrap', gap: 1 }}>
        <Typography variant="subtitle1" fontWeight={600}>
          Weekly Slot Template — {activeProvider?.fullName ?? 'Select a Provider'}
        </Typography>
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
          <FormControl size="small" sx={{ minWidth: 200 }}>
            <InputLabel id="provider-select-label">Provider</InputLabel>
            <Select
              labelId="provider-select-label"
              value={selectedProviderId}
              label="Provider"
              onChange={e => setSelectedProviderId(e.target.value)}
            >
              {providers.length === 0 && (
                <MenuItem value="" disabled>No providers found</MenuItem>
              )}
              {providers.map(p => (
                <MenuItem key={p.id} value={p.id}>{p.fullName}</MenuItem>
              ))}
            </Select>
          </FormControl>
          <Button
            variant="contained"
            size="small"
            onClick={() => void handleSaveAll()}
            disabled={isSaving || !selectedProviderId}
            aria-busy={isSaving}
            aria-label="Save slot template"
            startIcon={isSaving ? <CircularProgress size={14} color="inherit" /> : undefined}
          >
            {isSaving ? 'Saving…' : 'Save Template'}
          </Button>
        </Box>
      </Box>

      <Typography variant="caption" color="text.secondary" sx={{ mb: 1.5, display: 'block' }}>
        Click a slot to toggle availability. Green = available, strikethrough = blocked. Changes auto-save after 1.5s.
      </Typography>

      {/* ── Grid (7 days × 5 time rows) ── */}
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: `80px repeat(${COLUMN_DAYS.length}, 1fr)`,
          gap: '4px',
          fontSize: '0.8125rem',
          overflowX: 'auto',
        }}
        role="grid"
        aria-label="Weekly slot availability grid"
      >
        {/* Header row */}
        <Box />
        {COLUMN_DAYS.map(col => (
          <Box key={col.label} sx={{ fontWeight: 600, p: 0.75, textAlign: 'center' }} role="columnheader">
            {col.label}
          </Box>
        ))}

        {/* Data rows */}
        {TIME_ROWS.map(row => (
          <>
            <Box
              key={`label-${row.apiTime}`}
              sx={{ p: 0.75, fontWeight: 500, display: 'flex', alignItems: 'center' }}
              role="rowheader"
            >
              {row.label}
            </Box>
            {COLUMN_DAYS.map(col => {
              const avail = cells[cellKey(col.dayOfWeek, row.apiTime)] ?? true;
              return (
                <Tooltip
                  key={`${col.dayOfWeek}-${row.apiTime}`}
                  title={avail ? 'Click to block' : 'Click to unblock'}
                >
                  <Box
                    role="gridcell"
                    tabIndex={0}
                    onClick={() => toggleCell(col.dayOfWeek, row.apiTime)}
                    onKeyDown={e => (e.key === 'Enter' || e.key === ' ') && toggleCell(col.dayOfWeek, row.apiTime)}
                    aria-pressed={avail}
                    aria-label={`${row.label} ${col.label}: ${avail ? 'Available' : 'Blocked'}`}
                    sx={{
                      p: '6px',
                      border: '1px solid',
                      borderColor: avail ? 'success.200' : 'grey.200',
                      borderRadius: 1,
                      textAlign: 'center',
                      cursor: 'pointer',
                      bgcolor: avail ? 'success.50' : 'grey.100',
                      color:   avail ? 'success.800' : 'text.disabled',
                      textDecoration: avail ? 'none' : 'line-through',
                      userSelect: 'none',
                      '&:hover':        { opacity: 0.8 },
                      '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main' },
                    }}
                  >
                    {avail ? 'Available' : 'Blocked'}
                  </Box>
                </Tooltip>
              );
            })}
          </>
        ))}
      </Box>

      {/* ── Conflict dialog (UXR-102) — 409 Conflict ── */}
      <Dialog
        open={conflictOpen}
        onClose={() => setConflictOpen(false)}
        aria-labelledby="conflict-dialog-title"
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle id="conflict-dialog-title" sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <WarningAmberIcon color="warning" />
          Template Conflict Detected
        </DialogTitle>
        <DialogContent>
          <Typography variant="body2" sx={{ mb: 1.5 }}>
            The slot template for{' '}
            <strong>{COLUMN_DAYS.find(c => c.dayOfWeek === conflictDay)?.label ?? 'this day'}</strong>{' '}
            was modified by another session. Reload to get the latest version, then re-apply changes.
          </Typography>
          <Chip
            label="Review existing appointments before retrying."
            color="warning"
            size="small"
            variant="outlined"
            sx={{ mt: 0.5 }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConflictOpen(false)} aria-label="Dismiss conflict warning">
            Dismiss
          </Button>
          <Button
            variant="contained"
            color="warning"
            onClick={() => {
              setConflictOpen(false);
              setCells({});
              setVersions({});
            }}
            aria-label="Reload template to resolve conflict"
          >
            Reload Template
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
