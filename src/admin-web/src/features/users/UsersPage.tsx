import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import FormLabel from '@mui/material/FormLabel';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useConfirm } from '../catalog/useConfirm';
import { useAuth } from '../auth/useAuth';
import { MIN_PASSWORD } from '../auth/ChangePasswordForm';
import { shopDateTime, usersApi, type RoleRef, type UserSummary } from './api';

type Editing =
  | { mode: 'create' }
  | { mode: 'edit'; id: string }
  | { mode: 'password'; user: UserSummary }
  | null;

export function UsersPage() {
  const { can, state } = useAuth();
  const canManage = can('users.manage');
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const [showDeleted, setShowDeleted] = useState(false);
  const [editing, setEditing] = useState<Editing>(null);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const { confirm, dialog } = useConfirm();
  const me = state.status === 'signedIn' ? state.user.id : null;

  const list = useQuery({
    queryKey: ['users', q, showDeleted],
    queryFn: () => usersApi.list({ q, includeDeleted: showDeleted }),
  });
  const run = useMutation({
    mutationFn: (action: () => Promise<unknown>) => action(),
    onSuccess: () => {
      setProblem(null);
      void queryClient.invalidateQueries({ queryKey: ['users'] });
    },
    onError: (error) => setProblem(describeError(error)),
  });

  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          Users
        </Typography>
        {canManage && (
          <Button
            variant="contained"
            onClick={() => setEditing({ mode: 'create' })}
          >
            New user
          </Button>
        )}
      </Stack>
      <p>Staff who can sign in to shop administration.</p>
      <Stack direction="row" spacing={2} sx={{ mb: 2, alignItems: 'center' }}>
        <TextField
          size="small"
          label="Search name or email"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
        {canManage && (
          <FormControlLabel
            control={
              <Switch
                checked={showDeleted}
                onChange={(e) => setShowDeleted(e.target.checked)}
              />
            }
            label="Show deleted"
          />
        )}
      </Stack>
      <ProblemAlert
        problem={list.isError ? describeError(list.error) : problem}
      />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Users">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Email</TableCell>
              <TableCell>Roles</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Last sign-in</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((user) => (
              <TableRow key={user.id}>
                <TableCell>{user.displayName}</TableCell>
                <TableCell>{user.email}</TableCell>
                <TableCell>{user.roles.join(', ') || '—'}</TableCell>
                <TableCell>
                  {user.isDeleted ? (
                    <Chip size="small" label="Deleted" />
                  ) : !user.isActive ? (
                    <Chip size="small" label="Inactive" />
                  ) : user.isLockedOut ? (
                    <Chip size="small" color="warning" label="Locked" />
                  ) : (
                    <Chip size="small" color="success" label="Active" />
                  )}
                </TableCell>
                <TableCell>{shopDateTime(user.lastLoginAt)}</TableCell>
                <TableCell align="right">
                  {canManage && !user.isDeleted && (
                    <>
                      <Button
                        size="small"
                        onClick={() =>
                          setEditing({ mode: 'edit', id: user.id })
                        }
                      >
                        Edit
                      </Button>
                      <Button
                        size="small"
                        onClick={() => setEditing({ mode: 'password', user })}
                      >
                        Reset password
                      </Button>
                      {user.isLockedOut && (
                        <Button
                          size="small"
                          onClick={() =>
                            run.mutate(() => usersApi.unlock(user.id))
                          }
                        >
                          Unlock
                        </Button>
                      )}
                    </>
                  )}
                  {can('permissions.manage') && !user.isDeleted && (
                    <Button
                      size="small"
                      component={Link}
                      to={`/users/${user.id}/permissions`}
                    >
                      Permissions
                    </Button>
                  )}
                  {canManage && !user.isDeleted && user.id !== me && (
                    <Button
                      size="small"
                      color="error"
                      onClick={() =>
                        confirm({
                          title: 'Delete user?',
                          text: `${user.displayName} will be signed out and can no longer sign in. You can restore the account later.`,
                          action: () =>
                            run.mutate(() => usersApi.remove(user.id)),
                        })
                      }
                    >
                      Delete
                    </Button>
                  )}
                  {canManage && user.isDeleted && (
                    <Button
                      size="small"
                      onClick={() =>
                        run.mutate(() => usersApi.restore(user.id))
                      }
                    >
                      Restore
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>No users found.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
      {dialog}
      {(editing?.mode === 'create' || editing?.mode === 'edit') && (
        <UserDialog
          id={editing.mode === 'edit' ? editing.id : null}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['users'] });
          }}
        />
      )}
      {editing?.mode === 'password' && (
        <ResetPasswordDialog
          user={editing.user}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  );
}

function RolePicker({
  options,
  value,
  onChange,
}: {
  options: RoleRef[];
  value: string[];
  onChange: (ids: string[]) => void;
}) {
  return (
    <fieldset className="plain-fieldset">
      <FormLabel component="legend">Roles</FormLabel>
      <FormGroup row>
        {options.map((role) => (
          <FormControlLabel
            key={role.id}
            label={role.name}
            control={
              <Checkbox
                checked={value.includes(role.id)}
                onChange={(e) =>
                  onChange(
                    e.target.checked
                      ? [...value, role.id]
                      : value.filter((x) => x !== role.id),
                  )
                }
              />
            }
          />
        ))}
      </FormGroup>
    </fieldset>
  );
}

function UserDialog({
  id,
  onClose,
  onSaved,
}: {
  id: string | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const existing = useQuery({
    queryKey: ['user', id],
    queryFn: () => usersApi.get(id!),
    enabled: id !== null,
  });
  const roles = useQuery({
    queryKey: ['role-options'],
    queryFn: usersApi.roleOptions,
  });
  const user = existing.data;
  const [form, setForm] = useState<{
    email: string;
    displayName: string;
    password: string;
    isActive: boolean;
    roleIds: string[];
  } | null>(
    id === null
      ? {
          email: '',
          displayName: '',
          password: '',
          isActive: true,
          roleIds: [],
        }
      : null,
  );
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  if (form === null && user)
    setForm({
      email: user.email,
      displayName: user.displayName,
      password: '',
      isActive: user.isActive,
      roleIds: user.roles.map((r) => r.id),
    });

  const save = async () => {
    if (!form) return;
    setBusy(true);
    setProblem(null);
    try {
      if (id === null)
        await usersApi.create({
          email: form.email,
          displayName: form.displayName,
          temporaryPassword: form.password,
          roleIds: form.roleIds,
        });
      else {
        const updated = await usersApi.update(id, {
          email: form.email,
          displayName: form.displayName,
          isActive: form.isActive,
          version: user!.version,
        });
        const before = user!.roles
          .map((r) => r.id)
          .sort()
          .join();
        if (before !== [...form.roleIds].sort().join())
          await usersApi.setRoles(updated.id, form.roleIds);
      }
      onSaved();
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{id === null ? 'New user' : 'Edit user'}</DialogTitle>
      <DialogContent>
        {form && (
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ProblemAlert problem={problem} />
            <TextField
              label="Name"
              value={form.displayName}
              onChange={(e) =>
                setForm({ ...form, displayName: e.target.value })
              }
              error={!!fieldError(problem, 'displayName')}
              helperText={fieldError(problem, 'displayName')}
              required
            />
            <TextField
              label="Email"
              type="email"
              value={form.email}
              onChange={(e) => setForm({ ...form, email: e.target.value })}
              error={!!fieldError(problem, 'email')}
              helperText={fieldError(problem, 'email') ?? 'Used to sign in.'}
              required
            />
            {id === null && (
              <TextField
                label="Temporary password"
                type="password"
                autoComplete="new-password"
                value={form.password}
                onChange={(e) => setForm({ ...form, password: e.target.value })}
                error={!!fieldError(problem, 'temporaryPassword')}
                helperText={
                  fieldError(problem, 'temporaryPassword') ??
                  `At least ${MIN_PASSWORD} characters. They must change it when they first sign in.`
                }
                required
              />
            )}
            {id !== null && (
              <FormControlLabel
                control={
                  <Switch
                    checked={form.isActive}
                    onChange={(e) =>
                      setForm({ ...form, isActive: e.target.checked })
                    }
                  />
                }
                label={form.isActive ? 'Active' : 'Inactive (cannot sign in)'}
              />
            )}
            <RolePicker
              options={roles.data ?? []}
              value={form.roleIds}
              onChange={(roleIds) => setForm({ ...form, roleIds })}
            />
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          onClick={() => void save()}
          disabled={busy || !form}
        >
          {busy ? 'Saving…' : 'Save'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ResetPasswordDialog({
  user,
  onClose,
}: {
  user: UserSummary;
  onClose: () => void;
}) {
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [done, setDone] = useState(false);
  const save = async () => {
    try {
      await usersApi.resetPassword(user.id, password);
      setDone(true);
    } catch (error) {
      setProblem(describeError(error));
    }
  };
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Reset password</DialogTitle>
      <DialogContent>
        {done ? (
          <p>
            Done. {user.displayName} has been signed out everywhere and must
            choose a new password when they next sign in.
          </p>
        ) : (
          <Stack spacing={2} sx={{ pt: 1 }}>
            <ProblemAlert problem={problem} />
            <p>
              Set a temporary password for {user.displayName}. Give it to them
              in person or by phone.
            </p>
            <TextField
              label="Temporary password"
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              error={!!fieldError(problem, 'temporaryPassword')}
              helperText={
                fieldError(problem, 'temporaryPassword') ??
                `At least ${MIN_PASSWORD} characters.`
              }
            />
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{done ? 'Close' : 'Cancel'}</Button>
        {!done && (
          <Button
            variant="contained"
            disabled={password.length < MIN_PASSWORD}
            onClick={() => void save()}
          >
            Reset password
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
