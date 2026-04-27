/**
 * QuickActions — Walk-in Registration and View Queue action buttons (US_057 AC-1, SCR-010).
 *
 * Walk-in Registration opens the existing WalkInRegistrationModal (US_022 AC-1).
 * View Queue navigates to SCR-011 /staff/queue.
 *
 * Usage:
 *   <QuickActions onWalkIn={() => setWalkInOpen(true)} />
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import PeopleAltOutlinedIcon from '@mui/icons-material/PeopleAltOutlined';
import QueueOutlinedIcon from '@mui/icons-material/QueueOutlined';
import { Link as RouterLink } from 'react-router-dom';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  onWalkIn: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function QuickActions({ onWalkIn }: Props) {
  return (
    <Box
      sx={{ display: 'flex', gap: 2, mb: 4, flexWrap: 'wrap' }}
      role="group"
      aria-label="Quick actions"
    >
      <Button
        id="walkin-btn"
        variant="contained"
        color="secondary"
        startIcon={<PeopleAltOutlinedIcon />}
        onClick={onWalkIn}
        aria-haspopup="dialog"
      >
        Walk-in Registration
      </Button>
      <Button
        id="view-queue"
        variant="outlined"
        color="secondary"
        startIcon={<QueueOutlinedIcon />}
        component={RouterLink}
        to="/staff/queue"
      >
        View Queue
      </Button>
    </Box>
  );
}
