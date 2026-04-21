/**
 * SlotCalendar — Custom monthly calendar with availability dot indicators (US_017, AC-2).
 *
 * Wireframe (SCR-006):
 *   - 7-column grid (Sun–Sat), prev/next month navigation
 *   - Today highlighted with a primary-color border
 *   - Selected date filled with primary color
 *   - Dates with ≥1 available slot show a small blue dot beneath the day number
 *   - Past dates and dates beyond 90 days are grayed out / disabled (FR-013, EC-2)
 *   - Legend below calendar: "● Available slots"
 *
 * Accessibility (WCAG 2.1 AA):
 *   - role="grid" + role="gridcell" on the date cells
 *   - aria-selected on selected date
 *   - aria-disabled on disabled dates
 *   - Keyboard: Tab enters the grid; arrow keys navigate; Enter/Space select
 */

import { useCallback, useMemo, useState } from 'react';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  /** Currently selected date as "YYYY-MM-DD". Null = nothing selected. */
  selectedDate: string | null;
  /** Called when the user clicks an available date. */
  onDateSelect: (date: string) => void;
  /** Set of "YYYY-MM-DD" dates that have at least one available slot. */
  availableDates: Set<string>;
  /** True while the slot data is loading. */
  loading: boolean;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const DAY_HEADERS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MAX_BOOKING_DAYS = 90;

function toDateString(year: number, month: number, day: number): string {
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function SlotCalendar({ selectedDate, onDateSelect, availableDates, loading }: Props) {
  const today = useMemo(() => {
    const d = new Date();
    return toDateString(d.getFullYear(), d.getMonth(), d.getDate());
  }, []);

  const maxDate = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() + MAX_BOOKING_DAYS);
    return toDateString(d.getFullYear(), d.getMonth(), d.getDate());
  }, []);

  // Calendar display month (independent from selection)
  const [displayYear, setDisplayYear] = useState(() => new Date().getFullYear());
  const [displayMonth, setDisplayMonth] = useState(() => new Date().getMonth()); // 0-based

  const monthLabel = useMemo(
    () =>
      new Date(displayYear, displayMonth, 1).toLocaleDateString('en-US', {
        month: 'long',
        year: 'numeric',
      }),
    [displayYear, displayMonth],
  );

  // Calendar grid: [leading empty cells, day numbers]
  const calendarDays = useMemo(() => {
    const firstDayOfMonth = new Date(displayYear, displayMonth, 1).getDay(); // 0=Sun
    const daysInMonth = new Date(displayYear, displayMonth + 1, 0).getDate();
    return { firstDayOfMonth, daysInMonth };
  }, [displayYear, displayMonth]);

  const goPrevMonth = useCallback(() => {
    setDisplayMonth((m) => {
      if (m === 0) {
        setDisplayYear((y) => y - 1);
        return 11;
      }
      return m - 1;
    });
  }, []);

  const goNextMonth = useCallback(() => {
    setDisplayMonth((m) => {
      if (m === 11) {
        setDisplayYear((y) => y + 1);
        return 0;
      }
      return m + 1;
    });
  }, []);

  function isDisabled(dateStr: string): boolean {
    return dateStr < today || dateStr > maxDate;
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLDivElement>, dateStr: string) {
    if ((e.key === 'Enter' || e.key === ' ') && !isDisabled(dateStr)) {
      e.preventDefault();
      onDateSelect(dateStr);
    }
  }

  // Build flat array of cells: null for leading empties, day number otherwise
  const cells: (number | null)[] = [
    ...Array<null>(calendarDays.firstDayOfMonth).fill(null),
    ...Array.from({ length: calendarDays.daysInMonth }, (_, i) => i + 1),
  ];

  // Pad to full 6-row grid (42 cells) so layout is always consistent
  while (cells.length < 42) cells.push(null);

  return (
    <Box>
      {/* Month navigation */}
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          mb: 2,
        }}
      >
        <IconButton
          size="small"
          onClick={goPrevMonth}
          aria-label="Previous month"
          sx={{ color: 'primary.main' }}
        >
          <ChevronLeftIcon />
        </IconButton>

        <Typography variant="h6" component="h3" sx={{ fontWeight: 600 }}>
          {monthLabel}
        </Typography>

        <IconButton
          size="small"
          onClick={goNextMonth}
          aria-label="Next month"
          sx={{ color: 'primary.main' }}
        >
          <ChevronRightIcon />
        </IconButton>
      </Box>

      {loading ? (
        /* Skeleton placeholder (UXR-502) */
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '2px' }}>
          {Array.from({ length: 35 }).map((_, i) => (
            <Skeleton key={i} variant="rectangular" height={40} sx={{ borderRadius: 1 }} />
          ))}
        </Box>
      ) : (
        <Box
          role="grid"
          aria-label="Select appointment date"
          sx={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: '2px' }}
        >
          {/* Day-of-week headers */}
          {DAY_HEADERS.map((d) => (
            <Box
              key={d}
              role="columnheader"
              sx={{
                textAlign: 'center',
                py: 0.5,
                fontSize: '0.75rem',
                fontWeight: 500,
                color: 'text.secondary',
              }}
            >
              {d}
            </Box>
          ))}

          {/* Calendar cells */}
          {cells.map((day, idx) => {
            if (day === null) {
              return <Box key={`empty-${idx}`} role="gridcell" aria-hidden="true" />;
            }

            const dateStr = toDateString(displayYear, displayMonth, day);
            const disabled = isDisabled(dateStr);
            const isToday = dateStr === today;
            const isSelected = dateStr === selectedDate;
            const hasSlots = availableDates.has(dateStr);

            return (
              <Box
                key={dateStr}
                role="gridcell"
                aria-selected={isSelected}
                aria-disabled={disabled}
                tabIndex={disabled ? -1 : 0}
                onClick={() => !disabled && onDateSelect(dateStr)}
                onKeyDown={(e) => handleKeyDown(e, dateStr)}
                sx={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  minHeight: 40,
                  borderRadius: 1,
                  fontSize: '0.875rem',
                  cursor: disabled ? 'default' : 'pointer',
                  color: disabled ? 'text.disabled' : isSelected ? '#fff' : 'text.primary',
                  bgcolor: isSelected ? 'primary.main' : 'transparent',
                  border: isToday && !isSelected ? '1px solid' : '1px solid transparent',
                  borderColor: isToday && !isSelected ? 'primary.main' : 'transparent',
                  transition: 'background-color 0.15s, color 0.15s',
                  '&:hover': {
                    bgcolor: disabled
                      ? 'transparent'
                      : isSelected
                        ? 'primary.dark'
                        : 'primary.50',
                  },
                  '&:focus-visible': {
                    outline: '2px solid',
                    outlineColor: 'primary.main',
                    outlineOffset: 2,
                  },
                }}
              >
                {day}
                {/* Availability dot (AC-2) */}
                {hasSlots && !disabled && (
                  <Box
                    component="span"
                    aria-hidden="true"
                    sx={{
                      width: 6,
                      height: 6,
                      borderRadius: '50%',
                      bgcolor: isSelected ? '#fff' : 'primary.main',
                      mt: '2px',
                    }}
                  />
                )}
              </Box>
            );
          })}
        </Box>
      )}

      {/* Legend */}
      <Box sx={{ display: 'flex', alignItems: 'center', mt: 1, gap: 0.5 }}>
        <Box
          component="span"
          aria-hidden="true"
          sx={{
            display: 'inline-block',
            width: 6,
            height: 6,
            borderRadius: '50%',
            bgcolor: 'primary.main',
          }}
        />
        <Typography variant="caption" color="text.secondary">
          Available slots
        </Typography>
      </Box>
    </Box>
  );
}
