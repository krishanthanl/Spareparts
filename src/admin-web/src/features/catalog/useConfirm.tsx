import { useState, type ReactNode } from 'react';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';

/** Confirms destructive actions before they run. */
export function useConfirm() {
  const [pending, setPending] = useState<{
    title: string;
    text: ReactNode;
    action: () => void;
  } | null>(null);
  const dialog = (
    <Dialog open={pending !== null} onClose={() => setPending(null)}>
      <DialogTitle>{pending?.title}</DialogTitle>
      <DialogContent>
        <DialogContentText>{pending?.text}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setPending(null)}>Cancel</Button>
        <Button
          color="error"
          variant="contained"
          onClick={() => {
            pending?.action();
            setPending(null);
          }}
        >
          Delete
        </Button>
      </DialogActions>
    </Dialog>
  );
  return { confirm: setPending, dialog };
}
