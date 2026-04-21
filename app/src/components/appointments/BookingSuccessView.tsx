/**
 * BookingSuccessView — Appointment confirmation screen displayed after 201 Created (AC-4).
 *
 * Shown as a full-content replacement of the booking form once the API confirms the
 * booking. Displays:
 *   - Success icon + "Appointment Confirmed" heading
 *   - Prominent booking reference number in a highlighted box
 *   - Appointment details: date (MM/DD/YYYY), time (12-hour AM/PM), provider, type
 *   - CTAs: "View in Dashboard" (navigates to /patient/dashboard) + "Add to Calendar" (US_025)
 *
 * The Add to Calendar action (US_025, FR-025) reuses AppointmentCalendarAction so the
 * interaction pattern is consistent with the dashboard appointment cards.
 *
 * Manual fallback (EC-1): All appointment details remain visible on the card so patients
 * can manually enter the event if their calendar app does not support .ics files.
 *
 * Design tokens (designsystem.md):
 *   - Success colour: #2E7D32 (semantic success)
 *   - Primary: #1976D2
 *
 * Accessibility: role="status" + aria-live="polite" announces the success content to
 * screen readers as soon as the component mounts (WCAG 2.1 AA).
 */

import { Link as RouterLink } from 'react-router-dom';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import AppointmentCalendarAction from '@/components/appointments/AppointmentCalendarAction';

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

export interface BookingSuccessProps {
  bookingReference: string;
  appointmentId: string;
  date: string;
  startTime: string;
  endTime: string;
  providerName: string;
  appointmentType: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BookingSuccessView({
  bookingReference,
  appointmentId,
  date,
  startTime,
  endTime,
  providerName,
  appointmentType,
}: BookingSuccessProps) {
  const rows: [string, string][] = [
    ['Date', formatDateFull(date)],
    ['Time', `${formatTime(startTime)} – ${formatTime(endTime)}`],
    ['Provider', providerName],
    ['Type', appointmentType],
  ];

  return (
    <Box
      role="status"
      aria-live="polite"
      sx={{ display: 'flex', justifyContent: 'center', mt: 4 }}
    >
      <Card sx={{ maxWidth: 480, width: '100%' }}>
        <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
          {/* Success heading */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2.5 }}>
            <CheckCircleOutlineIcon sx={{ color: '#2E7D32', fontSize: 36 }} aria-hidden="true" />
            <Typography variant="h5" component="h2" fontWeight={600}>
              Appointment Confirmed
            </Typography>
          </Box>

          {/* Booking reference */}
          <Box
            sx={{
              bgcolor: 'primary.50',
              borderRadius: 1,
              px: 2,
              py: 1.5,
              mb: 3,
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              flexWrap: 'wrap',
              gap: 1,
            }}
          >
            <Typography variant="body2" color="text.secondary">
              Booking Reference
            </Typography>
            <Typography
              variant="body1"
              fontWeight={700}
              color="primary.main"
              aria-label={`Booking reference ${bookingReference}`}
            >
              {bookingReference}
            </Typography>
          </Box>

          {/* Appointment details */}
          <Divider sx={{ mb: 2 }} />
          {rows.map(([label, value]) => (
            <Box
              key={label}
              sx={{ display: 'flex', justifyContent: 'space-between', mb: 1.5, gap: 2 }}
            >
              <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
                {label}
              </Typography>
              <Typography variant="body2" fontWeight={500} textAlign="right">
                {value}
              </Typography>
            </Box>
          ))}

          {/* CTAs */}
          <Divider sx={{ my: 2 }} />
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
            <Button variant="contained" component={RouterLink} to="/patient/dashboard">
              View in Dashboard
            </Button>
            {/* Add to Calendar — downloads .ics for Google Calendar / Outlook (US_025, FR-025) */}
            <AppointmentCalendarAction
              appointmentId={appointmentId}
              bookingReference={bookingReference}
              variant="outlined"
            />
          </Box>
        </CardContent>
      </Card>
    </Box>
  );
}
