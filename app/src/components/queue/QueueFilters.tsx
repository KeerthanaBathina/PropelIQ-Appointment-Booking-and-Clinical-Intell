/**
 * QueueFilters — Provider, Appointment Type, and Status filter dropdowns for SCR-011 (US_053/US_056).
 *
 * Renders three MUI Select dropdowns + a "Clear All Filters" button side-by-side in a Card:
 *   - Provider:         "All Providers" + unique provider names from live queue entries
 *   - Appointment Type: "All Types"     + unique appointment types from live queue entries
 *   - Status:           "All Statuses"  + the meaningful queue statuses
 *
 * Filter changes are emitted upward via callbacks; parent page owns filter state.
 * "Clear All Filters" is shown when any filter is non-default (US_056 AC-2 empty-state).
 *
 * Accessibility (UXR-206): Each select has an explicit label association via `inputProps.id`.
 *   The "Clear All Filters" button is only rendered when at least one filter is active so
 *   screen readers can rely on its presence as a definitive signal that filters are applied.
 */

import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import type { SelectChangeEvent } from '@mui/material/Select';
import ClearIcon from '@mui/icons-material/Clear';
import type { QueueEntry } from '@/hooks/useQueueData';

// ─── Constants ────────────────────────────────────────────────────────────────

const ALL_VALUE = 'all';

const STATUS_OPTIONS = [
  { value: ALL_VALUE,    label: 'All Statuses' },
  { value: 'waiting',    label: 'Waiting'      },
  { value: 'in_visit',   label: 'In Visit'     },
  { value: 'no_show',    label: 'No-Show'      },
  { value: 'scheduled',  label: 'Scheduled'    },
  { value: 'completed',  label: 'Completed'    },
  { value: 'cancelled',  label: 'Cancelled'    },
];

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  /** All queue entries — used to derive the unique provider and appointment type lists. */
  entries:                   QueueEntry[];
  selectedProvider:          string;
  selectedAppointmentType:   string;
  selectedStatus:            string;
  onProviderChange:          (value: string) => void;
  onAppointmentTypeChange:   (value: string) => void;
  onStatusChange:            (value: string) => void;
  /** Resets all three filters to "all". */
  onClearAll:                () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function QueueFilters({
  entries,
  selectedProvider,
  selectedAppointmentType,
  selectedStatus,
  onProviderChange,
  onAppointmentTypeChange,
  onStatusChange,
  onClearAll,
}: Props) {
  // Derive sorted unique provider names from live queue data
  const providerOptions = [
    { value: ALL_VALUE, label: 'All Providers' },
    ...[...new Set(entries.map((e) => e.providerName).filter(Boolean))]
      .sort()
      .map((name) => ({ value: name as string, label: name as string })),
  ];

  // Derive sorted unique appointment types from live queue data (US_056 AC-2)
  const typeOptions = [
    { value: ALL_VALUE, label: 'All Types' },
    ...[...new Set(entries.map((e) => e.appointmentType).filter(Boolean))]
      .sort()
      .map((t) => ({ value: t as string, label: t as string })),
  ];

  const hasActiveFilter =
    selectedProvider        !== ALL_VALUE ||
    selectedAppointmentType !== ALL_VALUE ||
    selectedStatus          !== ALL_VALUE;

  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
      <div
        style={{ display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'flex-end' }}
        role="group"
        aria-label="Queue filters"
      >
        {/* Provider filter */}
        <FormControl size="small" sx={{ minWidth: 180 }}>
          <InputLabel id="q-provider-label">Provider</InputLabel>
          <Select
            labelId="q-provider-label"
            inputProps={{ id: 'q-provider' }}
            value={selectedProvider}
            label="Provider"
            onChange={(e: SelectChangeEvent) => onProviderChange(e.target.value)}
          >
            {providerOptions.map((opt) => (
              <MenuItem key={opt.value} value={opt.value}>
                {opt.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        {/* Appointment Type filter (US_056 AC-2) */}
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="q-type-label">Type</InputLabel>
          <Select
            labelId="q-type-label"
            inputProps={{ id: 'q-type' }}
            value={selectedAppointmentType}
            label="Type"
            onChange={(e: SelectChangeEvent) => onAppointmentTypeChange(e.target.value)}
          >
            {typeOptions.map((opt) => (
              <MenuItem key={opt.value} value={opt.value}>
                {opt.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        {/* Status filter */}
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="q-status-label">Status</InputLabel>
          <Select
            labelId="q-status-label"
            inputProps={{ id: 'q-status' }}
            value={selectedStatus}
            label="Status"
            onChange={(e: SelectChangeEvent) => onStatusChange(e.target.value)}
          >
            {STATUS_OPTIONS.map((opt) => (
              <MenuItem key={opt.value} value={opt.value}>
                {opt.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        {/* Clear All Filters — shown only when at least one filter is active (US_056 AC-2) */}
        {hasActiveFilter && (
          <Button
            size="small"
            variant="text"
            color="inherit"
            startIcon={<ClearIcon fontSize="small" />}
            onClick={onClearAll}
            aria-label="Clear all filters"
            sx={{ mb: 0.25, textTransform: 'none', color: 'text.secondary' }}
          >
            Clear All
          </Button>
        )}
      </div>
    </Paper>
  );
}
