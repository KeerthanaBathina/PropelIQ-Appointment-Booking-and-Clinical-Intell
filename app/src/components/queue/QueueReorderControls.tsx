/**
 * QueueReorderControls — Arrow up/down buttons for keyboard-based queue reordering (US_054).
 *
 * Complements drag-and-drop as an accessible alternative (UXR-206).
 * Up button is disabled when the entry is first; down button when last.
 */

import IconButton from '@mui/material/IconButton';
import Box from '@mui/material/Box';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';

interface Props {
  queueId:    string;
  patientName: string;
  isFirst:    boolean;
  isLast:     boolean;
  isMutating: boolean;
  onMoveUp:   () => void;
  onMoveDown: () => void;
}

export default function QueueReorderControls({
  patientName,
  isFirst,
  isLast,
  isMutating,
  onMoveUp,
  onMoveDown,
}: Props) {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
      <IconButton
        size="small"
        onClick={onMoveUp}
        disabled={isFirst || isMutating}
        aria-label={`Move ${patientName} up`}
        aria-disabled={isFirst || isMutating}
        sx={{ padding: 0 }}
      >
        <ArrowUpwardIcon fontSize="small" />
      </IconButton>
      <IconButton
        size="small"
        onClick={onMoveDown}
        disabled={isLast || isMutating}
        aria-label={`Move ${patientName} down`}
        aria-disabled={isLast || isMutating}
        sx={{ padding: 0 }}
      >
        <ArrowDownwardIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
