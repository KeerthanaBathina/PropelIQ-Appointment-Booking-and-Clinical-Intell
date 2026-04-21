/**
 * SlotConflictModal — UXR-602 booking conflict error modal.
 *
 * Displayed when the booking API returns 409 Conflict (the slot was booked by another
 * patient between selection and confirmation). Presents an empathetic message and
 * up to 3 alternative available slots as keyboard-accessible selectable cards.
 *
 * Content copy per figma_spec.md UXR-602 guidelines:
 *   "That slot was just booked. Here are 3 similar options."
 *
 * Accessibility (WCAG 2.1 AA):
 *   - role="button" + tabIndex={0} on each alternative card
 *   - Enter / Space key activates card selection
 *   - aria-labelledby wires dialog title to the dialog element
 *
 * @param open                - Whether the dialog is open
 * @param alternatives        - Up to 3 alternative available slots from the 409 response
 * @param onSelectAlternative - Called with the chosen alternative slot
 * @param onClose             - Called when patient closes the modal or returns to calendar
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Typography from '@mui/material/Typography';
import type { AppointmentSlot } from '@/hooks/useAppointmentSlots';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatDateFull(dateStr: string): string {
  const [y, m, day] = dateStr.split('-').map(Number);
  return new Date(y, m - 1, day).toLocaleDateString('en-US', {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

function formatTime(time24: string): string {
  const [hStr, mStr] = time24.split(':');
  const h = parseInt(hStr, 10);
  const period = h >= 12 ? 'PM' : 'AM';
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  return `${hour12}:${mStr ?? '00'} ${period}`;
}

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  open: boolean;
  alternatives: AppointmentSlot[];
  onSelectAlternative: (slot: AppointmentSlot) => void;
  onClose: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function SlotConflictModal({
  open,
  alternatives,
  onSelectAlternative,
  onClose,
}: Props) {
  const limited = alternatives.slice(0, 3);

  return (
    <Dialog
      open={open}
      onClose={onClose}
      aria-labelledby="conflict-modal-title"
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle id="conflict-modal-title">Slot No Longer Available</DialogTitle>

      <DialogContent>
        <Typography variant="body2" sx={{ mb: limited.length > 0 ? 2 : 0 }}>
          That slot was just booked. Here are 3 similar options.
        </Typography>

        {limited.length > 0 && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            {limited.map((slot) => (
              <Box
                key={slot.slotId}
                role="button"
                tabIndex={0}
                onClick={() => onSelectAlternative(slot)}
                onKeyDown={(e: React.KeyboardEvent) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onSelectAlternative(slot);
                  }
                }}
                sx={{
                  p: 1.5,
                  border: '1px solid',
                  borderColor: 'grey.300',
                  borderRadius: 1,
                  cursor: 'pointer',
                  '&:hover': { borderColor: 'primary.main', bgcolor: 'primary.50' },
                  '&:focus-visible': {
                    outline: '2px solid',
                    outlineColor: 'primary.main',
                    outlineOffset: 2,
                  },
                }}
              >
                <Typography variant="body2" fontWeight={600}>
                  {formatDateFull(slot.date)} — {formatTime(slot.startTime)}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {slot.providerName} · {slot.appointmentType}
                </Typography>
              </Box>
            ))}
          </Box>
        )}

        {limited.length === 0 && (
          <Typography variant="body2" color="text.secondary">
            No alternative slots are currently available. Please return to the calendar to choose a
            different time.
          </Typography>
        )}
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose} variant="outlined">
          Return to Calendar
        </Button>
      </DialogActions>
    </Dialog>
  );
}
