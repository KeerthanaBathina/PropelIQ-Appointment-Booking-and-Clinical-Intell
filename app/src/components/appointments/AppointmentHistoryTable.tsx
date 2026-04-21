/**
 * AppointmentHistoryTable — sortable, paginated appointment history table (US_024).
 *
 * Implements SCR-007 wireframe:
 *   - Columns: Date, Time, Provider, Type, Status
 *   - Date column header is sortable (AC-2): click toggles asc/desc; chevron icon indicates direction
 *   - Cancelled rows are rendered at 0.7 opacity (EC-2)
 *   - 10 items per page (AC-3); Previous / Next pagination (AC-3)
 *   - On mobile (< md breakpoint) renders a compact card list instead of table
 *
 * Accessibility (UXR-201, UXR-202, UXR-203):
 *   - table has aria-label
 *   - sortable Date column header has aria-sort attribute
 *   - pagination buttons have aria-label and aria-current
 *   - Previous/Next disabled state communicated via disabled + aria-disabled
 *
 * @param pagedAppointments  Slice of appointments for the current page
 * @param totalPages         Total page count
 * @param page               Current 1-based page number
 * @param sortDirection      Current sort direction for the date column
 * @param onPageChange       Called when the user navigates to a different page
 * @param onSortToggle       Called when the user clicks the Date column header
 */

import Box from '@mui/material/Box';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import AppointmentStatusBadge from './AppointmentStatusBadge';
import AppointmentCard from './AppointmentCard';
import type { PatientAppointment } from '@/hooks/usePatientAppointments';
import type { SortDirection } from '@/hooks/useAppointmentHistory';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatLocalDate(utcIso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    year:  'numeric',
    month: 'short',
    day:   'numeric',
  }).format(new Date(utcIso));
}

function formatLocalTime(utcIso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIso));
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  pagedAppointments: PatientAppointment[];
  totalPages: number;
  page: number;
  sortDirection: SortDirection;
  onPageChange: (p: number) => void;
  onSortToggle: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AppointmentHistoryTable({
  pagedAppointments,
  totalPages,
  page,
  sortDirection,
  onPageChange,
  onSortToggle,
}: Props) {
  const theme   = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));

  // ─── Mobile: card list ────────────────────────────────────────────────────
  if (isMobile) {
    return (
      <Box>
        {pagedAppointments.map((appt) => (
          <AppointmentCard key={appt.id} appointment={appt} />
        ))}
        <PaginationControls
          page={page}
          totalPages={totalPages}
          onPageChange={onPageChange}
        />
      </Box>
    );
  }

  // ─── Desktop: table view ──────────────────────────────────────────────────
  const ariaSort = sortDirection === 'desc' ? 'descending' : 'ascending';

  return (
    <Box>
      <TableContainer component={Paper} variant="outlined">
        <Table aria-label="Appointment history">
          <TableHead>
            <TableRow>
              {/* Sortable Date column (AC-2) */}
              <TableCell
                aria-sort={ariaSort}
                onClick={onSortToggle}
                sx={{
                  cursor:    'pointer',
                  userSelect: 'none',
                  whiteSpace: 'nowrap',
                  '&:hover':  { bgcolor: 'action.hover' },
                  fontWeight: 600,
                }}
              >
                <Box
                  component="span"
                  sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}
                >
                  Date
                  <Tooltip
                    title={
                      sortDirection === 'desc'
                        ? 'Currently sorted: newest first. Click for oldest first.'
                        : 'Currently sorted: oldest first. Click for newest first.'
                    }
                    arrow
                  >
                    {sortDirection === 'desc' ? (
                      <ArrowDownwardIcon
                        fontSize="small"
                        sx={{ fontSize: '0.875rem', color: 'primary.main' }}
                        aria-label="Sorted descending"
                      />
                    ) : (
                      <ArrowUpwardIcon
                        fontSize="small"
                        sx={{ fontSize: '0.875rem', color: 'primary.main' }}
                        aria-label="Sorted ascending"
                      />
                    )}
                  </Tooltip>
                </Box>
              </TableCell>

              <TableCell sx={{ fontWeight: 600 }}>Time</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Provider</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Type</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Status</TableCell>
            </TableRow>
          </TableHead>

          <TableBody>
            {pagedAppointments.map((appt) => (
              <TableRow
                key={appt.id}
                hover
                sx={{
                  // EC-2: Cancelled rows are visible but muted
                  opacity: appt.status === 'Cancelled' ? 0.7 : 1,
                }}
                aria-label={`${appt.status} appointment on ${formatLocalDate(appt.appointmentTime)} with ${appt.providerName}`}
              >
                <TableCell>{formatLocalDate(appt.appointmentTime)}</TableCell>
                <TableCell>{formatLocalTime(appt.appointmentTime)}</TableCell>
                <TableCell>{appt.providerName}</TableCell>
                <TableCell>{appt.appointmentType}</TableCell>
                <TableCell>
                  <AppointmentStatusBadge status={appt.status} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <PaginationControls
        page={page}
        totalPages={totalPages}
        onPageChange={onPageChange}
      />
    </Box>
  );
}

// ─── Pagination controls ──────────────────────────────────────────────────────

interface PaginationControlsProps {
  page: number;
  totalPages: number;
  onPageChange: (p: number) => void;
}

function PaginationControls({ page, totalPages, onPageChange }: PaginationControlsProps) {
  const canPrev = page > 1;
  const canNext = page < totalPages;

  return (
    <>
      <Divider />
      <Box
        component="nav"
        aria-label="Pagination"
        sx={{
          display:        'flex',
          alignItems:     'center',
          justifyContent: 'center',
          gap:            1,
          py:             1.5,
        }}
      >
        {/* Previous */}
        <IconButton
          size="small"
          onClick={() => onPageChange(page - 1)}
          disabled={!canPrev}
          aria-label="Previous page"
          aria-disabled={!canPrev}
        >
          <ChevronLeftIcon />
        </IconButton>

        {/* Page number buttons */}
        {Array.from({ length: totalPages }, (_, i) => i + 1).map((p) => (
          <Box
            key={p}
            component="button"
            onClick={() => onPageChange(p)}
            aria-label={`Page ${p}`}
            aria-current={p === page ? 'page' : undefined}
            sx={{
              minWidth:     32,
              height:       32,
              border:       '1px solid',
              borderRadius: 1,
              cursor:       'pointer',
              fontWeight:   p === page ? 700 : 400,
              bgcolor:      p === page ? 'primary.main' : 'background.paper',
              color:        p === page ? 'primary.contrastText' : 'text.primary',
              borderColor:  p === page ? 'primary.main' : 'divider',
              '&:hover':    { bgcolor: p === page ? 'primary.dark' : 'action.hover' },
              transition:   'background-color 0.15s',
            }}
          >
            {p}
          </Box>
        ))}

        {/* Next */}
        <IconButton
          size="small"
          onClick={() => onPageChange(page + 1)}
          disabled={!canNext}
          aria-label="Next page"
          aria-disabled={!canNext}
        >
          <ChevronRightIcon />
        </IconButton>
      </Box>
    </>
  );
}
