/**
 * PriorityOverrideDialog — Confirmation dialog when a non-urgent patient is moved
 * above an urgent patient (UXR-102 destructive action confirmation pattern, US_054).
 *
 * Shown when drag-and-drop or arrow-key reorder would place a normal-priority patient
 * before an urgent one — warns staff that care quality may be affected.
 */

import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import Box from '@mui/material/Box';

interface Props {
  open:        boolean;
  patientName: string;
  onConfirm:   () => void;
  onCancel:    () => void;
}

export default function PriorityOverrideDialog({ open, patientName, onConfirm, onCancel }: Props) {
  return (
    <Dialog
      open={open}
      onClose={onCancel}
      aria-labelledby="priority-override-dialog-title"
      aria-describedby="priority-override-dialog-description"
    >
      <DialogTitle id="priority-override-dialog-title">
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <WarningAmberIcon color="warning" />
          Confirm Priority Override
        </Box>
      </DialogTitle>
      <DialogContent>
        <DialogContentText id="priority-override-dialog-description">
          Moving <strong>{patientName}</strong> above an urgent patient may affect care quality.
          Are you sure you want to proceed?
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel} color="inherit">
          Cancel
        </Button>
        <Button onClick={onConfirm} color="warning" variant="contained" autoFocus>
          Move Anyway
        </Button>
      </DialogActions>
    </Dialog>
  );
}
