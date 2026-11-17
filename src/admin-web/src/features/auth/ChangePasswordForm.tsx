import { useState, type FormEvent } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { useAuth } from './useAuth';

export const MIN_PASSWORD = 12;

/** Used both for the forced first change and from the account menu. */
export function ChangePasswordForm({ onDone }: { onDone?: () => void }) {
  const { changePassword } = useAuth();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const mismatch = confirm.length > 0 && confirm !== next;
  const tooShort = next.length > 0 && next.length < MIN_PASSWORD;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setProblem(null);
    try {
      await changePassword(current, next);
      onDone?.();
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Stack component="form" spacing={2} onSubmit={submit} noValidate>
      {problem && (
        <Alert severity="error" role="alert">
          {problem.message}
        </Alert>
      )}
      <TextField
        label="Current password"
        type="password"
        autoComplete="current-password"
        value={current}
        onChange={(e) => setCurrent(e.target.value)}
        error={!!fieldError(problem, 'currentPassword')}
        helperText={fieldError(problem, 'currentPassword')}
        required
      />
      <TextField
        label="New password"
        type="password"
        autoComplete="new-password"
        value={next}
        onChange={(e) => setNext(e.target.value)}
        error={tooShort || !!fieldError(problem, 'newPassword')}
        helperText={
          fieldError(problem, 'newPassword') ??
          `At least ${MIN_PASSWORD} characters. A short phrase works well.`
        }
        required
      />
      <TextField
        label="Repeat new password"
        type="password"
        autoComplete="new-password"
        value={confirm}
        onChange={(e) => setConfirm(e.target.value)}
        error={mismatch}
        helperText={mismatch ? 'The passwords do not match.' : ' '}
        required
      />
      <Button
        type="submit"
        variant="contained"
        disabled={
          busy || !current || next.length < MIN_PASSWORD || next !== confirm
        }
      >
        {busy ? 'Saving…' : 'Change password'}
      </Button>
    </Stack>
  );
}
