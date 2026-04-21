/**
 * TimeSlotGrid — 4-column grid of bookable time slots (US_017, AC-3).
 *
 * Wireframe (SCR-006):
 *   - 4 columns on ≥768px, 2 columns on mobile (responsive)
 *   - Available slot: outlined card, hover shows primary-color border
 *   - Selected slot: primary-color filled
 *   - Unavailable slot: gray background, not interactive
 *   - Skeleton placeholders while loading (UXR-502 — shown after 300ms delay via CSS)
 *   - Empty state: "No available slots" + "Try Different Date" CTA (EC-1)
 *
 * Accessibility (WCAG 2.1 AA):
 *   - role="radiogroup" on the container, role="radio" on each slot
 *   - aria-checked + tabindex roving focus pattern
 *   - aria-disabled for unavailable slots
 *
 * UXR-401: scheduled status uses #1976D2 (primary.main).
 *
 * @param slots         - Slots to display (already filtered by date by caller).
 * @param selectedSlotId - Currently selected slot ID; null = nothing selected.
 * @param onSlotSelect  - Called with slotId when user picks a slot.
 * @param loading       - True while data is being fetched.
 * @param selectedDate  - ISO date label used in the section heading.
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import type { AppointmentSlot } from '@/hooks/useAppointmentSlots';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatTime(time24: string): string {
  const [hStr, mStr] = time24.split(':');
  const h = parseInt(hStr, 10);
  const m = mStr ?? '00';
  const period = h >= 12 ? 'PM' : 'AM';
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  return `${hour12}:${m} ${period}`;
}

function formatDateLabel(dateStr: string | null): string {
  if (!dateStr) return '';
  const [year, month, day] = dateStr.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  slots: AppointmentSlot[];
  selectedSlotId: string | null;
  onSlotSelect: (slotId: string) => void;
  onTryDifferentDate: () => void;
  loading: boolean;
  selectedDate: string | null;
  /**
   * Slot ID currently held via the hold API (UXR-503).
   * This slot renders a "Reserved" badge with primary.300-like colour
   * to provide immediate visual feedback of the hold reservation.
   */
  heldSlotId?: string | null;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function TimeSlotGrid({
  slots,
  selectedSlotId,
  onSlotSelect,
  onTryDifferentDate,
  loading,
  selectedDate,
  heldSlotId,
}: Props) {
  const dateLabel = formatDateLabel(selectedDate);

  // Roving tabIndex: only the selected slot (or first available) has tabIndex=0
  const firstAvailableId = slots.find((s) => s.available)?.slotId ?? null;

  function handleKeyDown(e: React.KeyboardEvent<HTMLDivElement>, slot: AppointmentSlot) {
    if ((e.key === 'Enter' || e.key === ' ') && slot.available) {
      e.preventDefault();
      onSlotSelect(slot.slotId);
    }
  }

  const headingText = selectedDate
    ? `Available Slots — ${dateLabel}`
    : 'Select a date to view slots';

  if (loading) {
    return (
      <Box>
        <Typography variant="h6" component="h3" sx={{ mb: 2, fontWeight: 600 }}>
          {headingText}
        </Typography>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(4, 1fr)' },
            gap: 1,
          }}
        >
          {Array.from({ length: 12 }).map((_, i) => (
            <Skeleton key={i} variant="rectangular" height={40} sx={{ borderRadius: 1 }} />
          ))}
        </Box>
      </Box>
    );
  }

  if (!selectedDate) {
    return (
      <Box>
        <Typography variant="h6" component="h3" sx={{ mb: 2, fontWeight: 600 }}>
          {headingText}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Choose a date on the calendar to see available time slots.
        </Typography>
      </Box>
    );
  }

  if (slots.length === 0) {
    // EC-1: empty state
    return (
      <Box>
        <Typography variant="h6" component="h3" sx={{ mb: 2, fontWeight: 600 }}>
          {headingText}
        </Typography>
        <Box
          sx={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'flex-start',
            gap: 2,
            py: 3,
          }}
        >
          <Typography variant="body2" color="text.secondary">
            No available slots for this date.
          </Typography>
          <Button variant="outlined" size="small" onClick={onTryDifferentDate}>
            Try Different Date
          </Button>
        </Box>
      </Box>
    );
  }

  return (
    <Box>
      <Typography variant="h6" component="h3" sx={{ mb: 0.5, fontWeight: 600 }}>
        {headingText}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Select a time slot
      </Typography>

      <Box
        role="radiogroup"
        aria-label="Select appointment time"
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(4, 1fr)' },
          gap: 1,
        }}
      >
        {slots.map((slot) => {
          const isSelected = slot.slotId === selectedSlotId;
          const isHeld = heldSlotId != null && slot.slotId === heldSlotId;
          const isFocusable = isSelected ? true : !selectedSlotId && slot.slotId === firstAvailableId;

          return (
            <Box
              key={slot.slotId}
              role="radio"
              aria-checked={isSelected || isHeld}
              aria-disabled={!slot.available}
              tabIndex={slot.available ? (isFocusable ? 0 : -1) : -1}
              onClick={() => slot.available && onSlotSelect(slot.slotId)}
              onKeyDown={(e) => handleKeyDown(e, slot)}
              title={
                isHeld
                  ? 'Reserved — confirm booking in the dialog'
                  : slot.available
                    ? `${slot.providerName} — ${slot.appointmentType}`
                    : 'Unavailable'
              }
              sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                minHeight: 40,
                px: 1.5,
                py: 1,
                border: '1px solid',
                borderRadius: 1,
                fontSize: '0.875rem',
                textAlign: 'center',
                cursor: slot.available ? 'pointer' : 'default',
                userSelect: 'none',
                // UXR-503: held slot uses primary.300-equivalent tint (#1976D2 @ 20%)
                borderColor: isHeld
                  ? 'primary.light'
                  : isSelected
                    ? 'primary.main'
                    : slot.available
                      ? 'grey.400'
                      : 'transparent',
                bgcolor: isHeld
                  ? 'rgba(25, 118, 210, 0.12)'
                  : isSelected
                    ? 'primary.main'
                    : slot.available
                      ? 'transparent'
                      : 'grey.200',
                color: isHeld
                  ? 'primary.main'
                  : isSelected
                    ? '#fff'
                    : slot.available
                      ? 'text.primary'
                      : 'text.disabled',
                transition: 'background-color 0.15s, border-color 0.15s',
                '&:hover': {
                  borderColor: slot.available && !isSelected && !isHeld ? 'primary.main' : undefined,
                  bgcolor: slot.available && !isSelected && !isHeld ? 'primary.50' : undefined,
                },
                '&:focus-visible': {
                  outline: '2px solid',
                  outlineColor: 'primary.main',
                  outlineOffset: 2,
                },
              }}
            >
              {formatTime(slot.startTime)}
              {/* UXR-503: "Reserved" badge for held slot */}
              {isHeld && (
                <Box
                  component="span"
                  sx={{
                    fontSize: '0.65rem',
                    fontWeight: 600,
                    color: 'primary.main',
                    lineHeight: 1,
                    mt: 0.25,
                    textTransform: 'uppercase',
                    letterSpacing: '0.03em',
                  }}
                >
                  Reserved
                </Box>
              )}
            </Box>
          );
        })}
      </Box>
    </Box>
  );
}
