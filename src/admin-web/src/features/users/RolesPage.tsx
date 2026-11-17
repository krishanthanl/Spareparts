import { useState } from 'react';
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
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useConfirm } from '../catalog/useConfirm';
import { usersApi, type PermissionDefinition, type Role } from './api';

export function RolesPage() {
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: ['roles'], queryFn: usersApi.roles });
  const catalog = useQuery({
    queryKey: ['permission-catalog'],
    queryFn: usersApi.permissionCatalog,
  });
  const [editing, setEditing] = useState<Role | 'new' | null>(null);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const { confirm, dialog } = useConfirm();
  const remove = useMutation({
    mutationFn: usersApi.deleteRole,
    onSuccess: () => {
      setProblem(null);
      void queryClient.invalidateQueries({ queryKey: ['roles'] });
    },
    onError: (error) => setProblem(describeError(error)),
  });
  const describe = (key: string) =>
    catalog.data?.find((p) => p.key === key)?.description ?? key;

  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          Roles
        </Typography>
        <Button variant="contained" onClick={() => setEditing('new')}>
          New role
        </Button>
      </Stack>
      <p>
        A role is a set of permissions. Give users one or more roles, then
        adjust single permissions per user if needed.
      </p>
      <ProblemAlert
        problem={roles.isError ? describeError(roles.error) : problem}
      />
      <Stack spacing={2}>
        {roles.data?.map((role) => (
          <Paper key={role.id} elevation={0} className="role-card">
            <Stack
              direction="row"
              sx={{
                justifyContent: 'space-between',
                alignItems: 'flex-start',
                gap: 2,
              }}
            >
              <div>
                <Typography variant="h6" component="h2">
                  {role.name}{' '}
                  {role.isSystem && <Chip size="small" label="System" />}
                </Typography>
                <p className="muted">
                  {role.description ?? 'No description.'} {role.userCount} user
                  {role.userCount === 1 ? '' : 's'}.
                </p>
              </div>
              <Stack direction="row">
                {!role.permissionsLocked && (
                  <Button onClick={() => setEditing(role)}>Edit</Button>
                )}
                {!role.isSystem && (
                  <Button
                    color="error"
                    onClick={() =>
                      confirm({
                        title: 'Delete role?',
                        text: `Delete the ${role.name} role?`,
                        action: () => remove.mutate(role.id),
                      })
                    }
                  >
                    Delete
                  </Button>
                )}
              </Stack>
            </Stack>
            <p>
              {role.permissionsLocked
                ? 'Every permission, always.'
                : role.permissions.length === 0
                  ? 'No permissions.'
                  : role.permissions.map(describe).join(' · ')}
            </p>
          </Paper>
        ))}
      </Stack>
      {dialog}
      {editing && catalog.data && (
        <RoleDialog
          role={editing === 'new' ? null : editing}
          catalog={catalog.data}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void queryClient.invalidateQueries({ queryKey: ['roles'] });
          }}
        />
      )}
    </>
  );
}

function RoleDialog({
  role,
  catalog,
  onClose,
  onSaved,
}: {
  role: Role | null;
  catalog: PermissionDefinition[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(role?.name ?? '');
  const [description, setDescription] = useState(role?.description ?? '');
  const [permissions, setPermissions] = useState<string[]>(
    role?.permissions ?? [],
  );
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const groups = [...new Set(catalog.map((p) => p.group))];

  const save = async () => {
    try {
      const input = { name, description: description || null, permissions };
      if (role)
        await usersApi.updateRole(role.id, { ...input, version: role.version });
      else await usersApi.createRole(input);
      onSaved();
    } catch (error) {
      setProblem(describeError(error));
    }
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{role ? `Edit ${role.name}` : 'New role'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <ProblemAlert problem={problem} />
          <TextField
            label="Name"
            value={name}
            disabled={role?.isSystem}
            onChange={(e) => setName(e.target.value)}
            error={!!fieldError(problem, 'name')}
            helperText={
              fieldError(problem, 'name') ??
              (role?.isSystem ? 'System roles keep their name.' : ' ')
            }
          />
          <TextField
            label="Description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
          {groups.map((group) => (
            <fieldset key={group} className="plain-fieldset">
              <legend>{group}</legend>
              <FormGroup>
                {catalog
                  .filter((p) => p.group === group)
                  .map((p) => (
                    <FormControlLabel
                      key={p.key}
                      label={p.description}
                      control={
                        <Checkbox
                          checked={permissions.includes(p.key)}
                          onChange={(e) =>
                            setPermissions(
                              e.target.checked
                                ? [...permissions, p.key]
                                : permissions.filter((x) => x !== p.key),
                            )
                          }
                        />
                      }
                    />
                  ))}
              </FormGroup>
            </fieldset>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!name.trim()}
          onClick={() => void save()}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
