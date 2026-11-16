import Alert from '@mui/material/Alert';
import type { ApiProblem } from './api';

export function ProblemAlert({ problem }: { problem: ApiProblem | null }) {
  if (!problem) return null;
  return (
    <Alert severity={problem.signInRequired ? 'info' : 'error'} role="alert">
      {problem.message}
    </Alert>
  );
}
