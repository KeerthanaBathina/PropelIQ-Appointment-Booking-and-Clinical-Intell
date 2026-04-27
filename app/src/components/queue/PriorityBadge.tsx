/**
 * PriorityBadge — standalone MUI Chip for queue entry priority (US_054, SCR-011).
 *
 * Design tokens (wireframe-SCR-011-arrival-queue.html):
 *   urgent  → bg #FFEBEE (error.surface), text #D32F2F (error.main), label "Urgent"
 *   normal  → bg #EEEEEE (neutral.200),   text #424242 (neutral.700), label "Normal"
 */

import Chip from '@mui/material/Chip';

interface Props {
  priority: 'urgent' | 'normal';
}

const STYLES = {
  urgent: {
    backgroundColor: '#FFEBEE',
    color: '#D32F2F',
    fontWeight: 600,
  },
  normal: {
    backgroundColor: '#EEEEEE',
    color: '#424242',
    fontWeight: 400,
  },
} as const;

export default function PriorityBadge({ priority }: Props) {
  return (
    <Chip
      label={priority === 'urgent' ? 'Urgent' : 'Normal'}
      size="small"
      sx={STYLES[priority]}
      aria-label={`Priority: ${priority}`}
    />
  );
}
