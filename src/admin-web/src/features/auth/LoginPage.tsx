import { useState, type FormEvent } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import axios from 'axios';
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { useAuth } from './useAuth';

function loginProblem(error: unknown): ApiProblem {
  // The API gives the same answer for every failed sign-in; show it as is.
  if (axios.isAxiosError(error) && error.response?.status === 401)
    return {
      message: 'Email or password is incorrect.',
      fieldErrors: {},
      signInRequired: false,
    };
  return describeError(error);
}

export function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ApiProblem | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setProblem(null);
    try {
      await login(email.trim(), password);
    } catch (error) {
      setProblem(loginProblem(error));
      setPassword('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login-page" id="main">
      <Paper
        component="form"
        className="login-card"
        onSubmit={submit}
        noValidate
      >
        <span className="brand-mark" aria-hidden="true">
          S
        </span>
        <Typography variant="h4" component="h1">
          Sign in
        </Typography>
        <p>Spareparts shop administration</p>
        <Stack spacing={2}>
          {problem && (
            <Alert severity="error" role="alert">
              {problem.message}
            </Alert>
          )}
          <TextField
            label="Email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={!!fieldError(problem, 'email')}
            helperText={fieldError(problem, 'email')}
            required
            autoFocus
          />
          <TextField
            label="Password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            error={!!fieldError(problem, 'password')}
            helperText={fieldError(problem, 'password')}
            required
          />
          <Button
            type="submit"
            variant="contained"
            size="large"
            disabled={busy || !email || !password}
          >
            {busy ? 'Signing in…' : 'Sign in'}
          </Button>
        </Stack>
      </Paper>
    </main>
  );
}
