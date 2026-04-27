/**
 * BusinessHoursPanel — Operating hours + holidays for SCR-015 Config tab (US_059 AC-3, AC-4).
 *
 * Wireframe: two-column layout — regular hours table (left) + holiday cards (right).
 * On mobile (<sm) columns stack vertically (UXR-303).
 * Holiday status badge: Closed=error, Half Day=warning.
 *
 * UXR-004: Edit mode for business hours (inline table editing).
 * UXR-102: Confirmation dialog before deleting a holiday.
 * UXR-501: Inline validation within 200ms (closeTime > openTime check on blur).
 * UXR-502: Skeleton loading.
 * AC-4: AddHolidayDialog shows affected appointments count on creation.
 *
 * API routes (US_059 BE):
 *   GET/PUT  /api/admin/config/business-hours  (structured 7-day schedule)
 *   GET/POST /api/admin/config/holidays
 *   DELETE   /api/admin/config/holidays/{id}
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import Skeleton from '@mui/material/Skeleton';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { useState } from 'react';
import {
  useStructuredBusinessHours,
  useUpdateStructuredBusinessHours,
  useHolidaysList,
  useAddHoliday,
  useRemoveHoliday,
} from '@/hooks/useAdminConfig';
import { useToast } from '@/components/common/ToastProvider';
import type {
  BusinessHoursEntryDto,
  CreateHolidayRequest,
  HolidayResponse,
} from '@/types/adminConfig';

// ─── Constants ────────────────────────────────────────────────────────────────

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** "09:00:00" → "09:00" for <input type="time"> */
const toInputValue = (t: string | null): string => (t ? t.slice(0, 5) : '');

/** "09:00" → "09:00:00" for API */
const fromInputValue = (t: string): string | null => (t ? `${t}:00` : null);

/** "09:00:00" → "9:00 AM" for display */
function toDisplayTime(t: string): string {
  const [hStr, m] = t.split(':');
  const h      = parseInt(hStr, 10);
  const suffix = h >= 12 ? 'PM' : 'AM';
  const hour   = h === 0 ? 12 : h > 12 ? h - 12 : h;
  return `${hour}:${m} ${suffix}`;
}

function formatDate(iso: string): string {
  try {
    return new Intl.DateTimeFormat('en-US', {
      month: 'long',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date(`${iso}T00:00:00`));
  } catch {
    return iso;
  }
}

// ─── Add Holiday Dialog ───────────────────────────────────────────────────────

interface AddHolidayDialogProps {
  open: boolean;
  onClose: () => void;
}

function AddHolidayDialog({ open, onClose }: AddHolidayDialogProps) {
  const { showToast } = useToast();
  const { mutateAsync: addHoliday, isLoading } = useAddHoliday();

  const [form, setForm] = useState<CreateHolidayRequest>({
    date:        '',
    name:        '',
    isRecurring: false,
    isHalfDay:   false,
  });
  const [nameError, setNameError] = useState('');
  const [dateError, setDateError] = useState('');
  // Affected appointments result after creation
  const [affectedCount, setAffectedCount] = useState<number | null>(null);

  function validate(): boolean {
    let ok = true;
    if (!form.name.trim()) { setNameError('Name is required.'); ok = false; }
    else setNameError('');
    if (!form.date) { setDateError('Date is required.'); ok = false; }
    else setDateError('');
    return ok;
  }

  async function handleSubmit() {
    if (!validate()) return;
    try {
      const result = await addHoliday(form);
      setAffectedCount(result.affectedAppointmentCount);
      if (result.affectedAppointmentCount === 0) {
        showToast({ message: `Holiday "${result.holiday.name}" added.`, severity: 'success' });
        handleClose();
      }
      // if count > 0, we stay open to show the warning before closing
    } catch {
      showToast({ message: 'Failed to add holiday. Please retry.', severity: 'error' });
    }
  }

  function handleClose() {
    setForm({ date: '', name: '', isRecurring: false, isHalfDay: false });
    setNameError('');
    setDateError('');
    setAffectedCount(null);
    onClose();
  }

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      aria-labelledby="add-holiday-title"
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle id="add-holiday-title">Add Holiday</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
        {affectedCount !== null && affectedCount > 0 && (
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', bgcolor: 'warning.50', p: 1.5, borderRadius: 1 }}>
            <WarningAmberIcon color="warning" fontSize="small" sx={{ mt: 0.25 }} />
            <Typography variant="body2">
              <strong>{affectedCount}</strong> existing appointment{affectedCount !== 1 ? 's' : ''} fall on this date and require staff review.
            </Typography>
          </Box>
        )}
        <TextField
          label="Holiday Name"
          value={form.name}
          onChange={e => {
            setForm(f => ({ ...f, name: e.target.value }));
            if (e.target.value.trim()) setNameError('');
          }}
          onBlur={() => { if (!form.name.trim()) setNameError('Name is required.'); }}
          error={!!nameError}
          helperText={nameError}
          size="small"
          inputProps={{ maxLength: 200, 'aria-label': 'Holiday name' }}
          required
          fullWidth
        />
        <TextField
          label="Date"
          type="date"
          value={form.date}
          onChange={e => {
            setForm(f => ({ ...f, date: e.target.value }));
            if (e.target.value) setDateError('');
          }}
          onBlur={() => { if (!form.date) setDateError('Date is required.'); }}
          error={!!dateError}
          helperText={dateError}
          size="small"
          InputLabelProps={{ shrink: true }}
          inputProps={{ 'aria-label': 'Holiday date' }}
          required
          fullWidth
        />
        <FormControlLabel
          control={
            <Checkbox
              checked={form.isRecurring}
              onChange={e => setForm(f => ({ ...f, isRecurring: e.target.checked }))}
              aria-label="Repeats annually"
            />
          }
          label="Repeats annually"
        />
        <FormControlLabel
          control={
            <Checkbox
              checked={form.isHalfDay}
              onChange={e => setForm(f => ({ ...f, isHalfDay: e.target.checked }))}
              aria-label="Half day"
            />
          }
          label="Half day"
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose} aria-label="Cancel add holiday">Cancel</Button>
        {affectedCount !== null && affectedCount > 0 ? (
          <Button
            variant="contained"
            color="warning"
            onClick={handleClose}
            aria-label="Acknowledge affected appointments and close"
          >
            Acknowledge &amp; Close
          </Button>
        ) : (
          <Button
            variant="contained"
            onClick={() => void handleSubmit()}
            disabled={isLoading}
            aria-label="Save holiday"
            startIcon={isLoading ? <CircularProgress size={14} color="inherit" /> : undefined}
          >
            {isLoading ? 'Saving…' : 'Save Holiday'}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BusinessHoursPanel() {
  const { showToast } = useToast();

  // ── Hours data
  const { data: hoursData, isLoading: hoursLoading } = useStructuredBusinessHours();
  const { mutateAsync: saveHours, isLoading: isSavingHours } = useUpdateStructuredBusinessHours();

  // Edit mode
  const [isEditing, setIsEditing] = useState(false);
  const [editedHours, setEditedHours] = useState<BusinessHoursEntryDto[]>([]);
  const [timeErrors, setTimeErrors] = useState<Record<number, string>>({});

  function startEdit() {
    setEditedHours(hoursData ? [...hoursData] : []);
    setTimeErrors({});
    setIsEditing(true);
  }

  function updateEntry(dayOfWeek: number, patch: Partial<BusinessHoursEntryDto>) {
    setEditedHours(prev =>
      prev.map(e => e.dayOfWeek === dayOfWeek ? { ...e, ...patch } : e),
    );
  }

  function validateEntry(e: BusinessHoursEntryDto): string {
    if (e.isClosed) return '';
    if (!e.openTime || !e.closeTime) return 'Open and close times are required.';
    if (e.openTime >= e.closeTime) return 'Close time must be after open time.';
    return '';
  }

  function handleTimeBlur(dayOfWeek: number) {
    const entry = editedHours.find(e => e.dayOfWeek === dayOfWeek);
    if (!entry) return;
    const err = validateEntry(entry);
    setTimeErrors(prev => ({ ...prev, [dayOfWeek]: err }));
  }

  async function handleSaveHours() {
    // Validate all entries
    const errors: Record<number, string> = {};
    for (const e of editedHours) {
      const err = validateEntry(e);
      if (err) errors[e.dayOfWeek] = err;
    }
    if (Object.keys(errors).length > 0) { setTimeErrors(errors); return; }

    try {
      await saveHours({ entries: editedHours });
      setIsEditing(false);
      showToast({ message: 'Business hours updated.', severity: 'success' });
    } catch {
      showToast({ message: 'Failed to save hours. Please retry.', severity: 'error' });
    }
  }

  // ── Holidays data
  const { data: holidaysData, isLoading: holidaysLoading } = useHolidaysList();
  const { mutateAsync: removeHoliday, isLoading: isRemoving } = useRemoveHoliday();

  // Delete confirmation
  const [deleteTarget, setDeleteTarget] = useState<HolidayResponse | null>(null);

  async function handleConfirmDelete() {
    if (!deleteTarget) return;
    try {
      await removeHoliday(deleteTarget.holidayId);
      showToast({ message: `Holiday "${deleteTarget.name}" removed.`, severity: 'success' });
    } catch {
      showToast({ message: 'Failed to remove holiday. Please retry.', severity: 'error' });
    } finally {
      setDeleteTarget(null);
    }
  }

  // Add holiday dialog
  const [addOpen, setAddOpen] = useState(false);

  // ── Loading state
  if (hoursLoading || holidaysLoading) {
    return (
      <Grid container spacing={3}>
        <Grid item xs={12} sm={6}><Skeleton variant="rectangular" height={200} /></Grid>
        <Grid item xs={12} sm={6}><Skeleton variant="rectangular" height={200} /></Grid>
      </Grid>
    );
  }

  const hours    = hoursData ?? [];
  const holidays = holidaysData ?? [];

  return (
    <Box>
      <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 2 }}>
        Operating Hours &amp; Holidays
      </Typography>

      {/* UXR-303: two-column on desktop, stacked on mobile */}
      <Grid container spacing={4}>

        {/* ── Regular hours ── */}
        <Grid item xs={12} sm={6}>
          <Typography variant="subtitle2" fontWeight={600} sx={{ mb: 1.5 }}>
            Regular Hours
          </Typography>

          {isEditing ? (
            /* Edit mode — time inputs */
            <Box component="table" sx={{ width: '100%', fontSize: '0.875rem', borderCollapse: 'collapse' }} aria-label="Edit operating hours">
              <Box component="tbody">
                {editedHours
                  .slice()
                  .sort((a, b) => (a.dayOfWeek === 0 ? 7 : a.dayOfWeek) - (b.dayOfWeek === 0 ? 7 : b.dayOfWeek))
                  .map(entry => (
                    <Box component="tr" key={entry.dayOfWeek} sx={{ '& td': { py: 0.75, verticalAlign: 'middle' } }}>
                      <Box component="td" sx={{ fontWeight: 500, pr: 1.5, whiteSpace: 'nowrap', minWidth: 100 }}>
                        {DAY_NAMES[entry.dayOfWeek]}
                      </Box>
                      <Box component="td">
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                          <FormControlLabel
                            control={
                              <Switch
                                size="small"
                                checked={entry.isClosed}
                                onChange={e => updateEntry(entry.dayOfWeek, { isClosed: e.target.checked })}
                                aria-label={`${DAY_NAMES[entry.dayOfWeek]} closed`}
                              />
                            }
                            label={<Typography variant="caption">Closed</Typography>}
                            sx={{ mr: 0 }}
                          />
                          {!entry.isClosed && (
                            <>
                              <input
                                type="time"
                                value={toInputValue(entry.openTime)}
                                onChange={e => updateEntry(entry.dayOfWeek, { openTime: fromInputValue(e.target.value) })}
                                onBlur={() => handleTimeBlur(entry.dayOfWeek)}
                                aria-label={`${DAY_NAMES[entry.dayOfWeek]} open time`}
                                style={{ fontSize: '0.8125rem', padding: '4px 6px', borderRadius: 4, border: '1px solid #ccc' }}
                              />
                              <Typography variant="caption">–</Typography>
                              <input
                                type="time"
                                value={toInputValue(entry.closeTime)}
                                onChange={e => updateEntry(entry.dayOfWeek, { closeTime: fromInputValue(e.target.value) })}
                                onBlur={() => handleTimeBlur(entry.dayOfWeek)}
                                aria-label={`${DAY_NAMES[entry.dayOfWeek]} close time`}
                                style={{ fontSize: '0.8125rem', padding: '4px 6px', borderRadius: 4, border: '1px solid #ccc' }}
                              />
                            </>
                          )}
                        </Box>
                        {timeErrors[entry.dayOfWeek] && (
                          <Typography variant="caption" color="error" role="alert">
                            {timeErrors[entry.dayOfWeek]}
                          </Typography>
                        )}
                      </Box>
                    </Box>
                  ))}
              </Box>
            </Box>
          ) : (
            /* Read mode */
            <Box component="table" sx={{ width: '100%', fontSize: '0.875rem', borderCollapse: 'collapse' }} aria-label="Regular operating hours">
              <Box component="tbody">
                {hours
                  .slice()
                  .sort((a, b) => (a.dayOfWeek === 0 ? 7 : a.dayOfWeek) - (b.dayOfWeek === 0 ? 7 : b.dayOfWeek))
                  .map(h => (
                    <Box component="tr" key={h.dayOfWeek} sx={{ '& td': { py: 0.5 } }}>
                      <Box component="td" sx={{ fontWeight: 500, pr: 2, whiteSpace: 'nowrap' }}>
                        {DAY_NAMES[h.dayOfWeek]}
                      </Box>
                      <Box component="td" sx={{ color: h.isClosed ? 'text.disabled' : 'text.primary' }}>
                        {h.isClosed
                          ? 'Closed'
                          : `${h.openTime ? toDisplayTime(h.openTime) : '?'} – ${h.closeTime ? toDisplayTime(h.closeTime) : '?'}`}
                      </Box>
                    </Box>
                  ))}
              </Box>
            </Box>
          )}

          <Box sx={{ display: 'flex', gap: 1, mt: 2 }}>
            {isEditing ? (
              <>
                <Button
                  size="small"
                  variant="contained"
                  onClick={() => void handleSaveHours()}
                  disabled={isSavingHours}
                  aria-label="Save business hours"
                  startIcon={isSavingHours ? <CircularProgress size={14} color="inherit" /> : undefined}
                >
                  {isSavingHours ? 'Saving…' : 'Save Hours'}
                </Button>
                <Button
                  size="small"
                  variant="outlined"
                  onClick={() => setIsEditing(false)}
                  aria-label="Cancel editing hours"
                >
                  Cancel
                </Button>
              </>
            ) : (
              <Button size="small" variant="outlined" onClick={startEdit} aria-label="Edit regular hours">
                Edit Hours
              </Button>
            )}
          </Box>
        </Grid>

        {/* ── Holidays ── */}
        <Grid item xs={12} sm={6}>
          <Typography variant="subtitle2" fontWeight={600} sx={{ mb: 1.5 }}>
            Upcoming Holidays
          </Typography>

          {holidays.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
              No holidays configured.
            </Typography>
          )}

          {holidays.map(h => (
            <Box
              key={h.holidayId}
              sx={{
                display:        'flex',
                alignItems:     'center',
                justifyContent: 'space-between',
                p:              1.5,
                border:         '1px solid',
                borderColor:    'divider',
                borderRadius:   1,
                mb:             1,
              }}
              aria-label={`${h.name}: ${formatDate(h.date)}, ${h.isHalfDay ? 'Half Day' : 'Closed'}`}
            >
              <Box sx={{ minWidth: 0, mr: 1 }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                  <Typography variant="body2" fontWeight={500} noWrap>{h.name}</Typography>
                  {h.isRecurring && (
                    <Chip label="Annual" size="small" variant="outlined" color="default" sx={{ fontSize: '0.65rem', height: 18 }} />
                  )}
                </Box>
                <Typography variant="caption" color="text.secondary">{formatDate(h.date)}</Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexShrink: 0 }}>
                <Chip
                  label={h.isHalfDay ? 'Half Day' : 'Closed'}
                  size="small"
                  color={h.isHalfDay ? 'warning' : 'error'}
                  variant="outlined"
                />
                <Tooltip title="Remove holiday">
                  <IconButton
                    size="small"
                    onClick={() => setDeleteTarget(h)}
                    aria-label={`Delete holiday ${h.name}`}
                    disabled={isRemoving}
                  >
                    <DeleteOutlineIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </Box>
            </Box>
          ))}

          <Button
            size="small"
            variant="outlined"
            onClick={() => setAddOpen(true)}
            sx={{ mt: 1 }}
            aria-label="Add holiday"
          >
            + Add Holiday
          </Button>
        </Grid>
      </Grid>

      {/* ── Add Holiday Dialog ── */}
      <AddHolidayDialog open={addOpen} onClose={() => setAddOpen(false)} />

      {/* ── Delete Confirmation Dialog (UXR-102) ── */}
      <Dialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        aria-labelledby="delete-holiday-title"
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle id="delete-holiday-title" sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <WarningAmberIcon color="error" />
          Remove Holiday?
        </DialogTitle>
        <DialogContent>
          <Typography variant="body2">
            Are you sure you want to remove{' '}
            <strong>{deleteTarget?.name}</strong> ({deleteTarget ? formatDate(deleteTarget.date) : ''})?
            This action cannot be undone.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteTarget(null)} aria-label="Cancel delete">Cancel</Button>
          <Button
            variant="contained"
            color="error"
            onClick={() => void handleConfirmDelete()}
            disabled={isRemoving}
            aria-label="Confirm delete holiday"
            startIcon={isRemoving ? <CircularProgress size={14} color="inherit" /> : undefined}
          >
            {isRemoving ? 'Removing…' : 'Remove'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
