import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { describeError, type ApiProblem } from '../../api/problems';
import { ProblemAlert } from '../catalog/ProblemAlert';
import {
  usersApi,
  type OverrideState,
  type PermissionMenu,
  type PermissionState,
  effectiveAccess,
} from './api';

function PermissionRow({
  label,
  detail,
  permission,
  state,
  locked,
  onChange,
}: {
  label: string;
  detail?: string;
  permission: PermissionState;
  state: OverrideState;
  locked: boolean;
  onChange: (state: OverrideState) => void;
}) {
  const effective = locked || effectiveAccess(permission.fromRoles, state);
  return (
    <div className="permission-row">
      <Checkbox
        checked={effective}
        disabled
        slotProps={{ input: { 'aria-label': `${label} allowed` } }}
      />
      <div className="permission-text">
        <strong>{label}</strong>
        <span>
          {detail ?? permission.description}
          {' · '}
          {permission.fromRoles ? 'allowed by role' : 'not in role'}
        </span>
      </div>
      <ToggleButtonGroup
        size="small"
        exclusive
        value={state}
        disabled={locked}
        aria-label={`${label} override`}
        onChange={(_, value: OverrideState | null) => value && onChange(value)}
      >
        <ToggleButton value="Inherit">Role</ToggleButton>
        <ToggleButton value="Allow">Allow</ToggleButton>
        <ToggleButton value="Deny">Deny</ToggleButton>
      </ToggleButtonGroup>
    </div>
  );
}

export function UserPermissionsPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const user = useQuery({
    queryKey: ['user', id],
    queryFn: () => usersApi.get(id),
  });
  const data = useQuery({
    queryKey: ['user-permissions', id],
    queryFn: () => usersApi.permissions(id),
  });
  const [changes, setChanges] = useState<Record<string, OverrideState>>({});
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);

  if (data.isError || user.isError)
    return <ProblemAlert problem={describeError(data.error ?? user.error)} />;
  if (!data.data || !user.data) return <p role="status">Loading…</p>;

  const byKey = new Map(data.data.permissions.map((p) => [p.key, p]));
  const stateOf = (key: string) => changes[key] ?? byKey.get(key)!.override;
  const locked = data.data.isSuperAdmin;
  const onMenus = new Set<string>();
  const collect = (nodes: PermissionMenu[]) =>
    nodes.forEach((n) => {
      if (n.permission) onMenus.add(n.permission);
      collect(n.children);
    });
  collect(data.data.menus);
  const others = data.data.permissions.filter((p) => !onMenus.has(p.key));
  const set = (key: string, value: OverrideState) => {
    setSaved(false);
    setChanges({ ...changes, [key]: value });
  };

  const renderMenu = (node: PermissionMenu, depth: number) => (
    <div key={node.key} style={{ marginLeft: depth * 24 }}>
      {node.permission && byKey.has(node.permission) ? (
        <PermissionRow
          label={node.title}
          detail={byKey.get(node.permission)!.description}
          permission={byKey.get(node.permission)!}
          state={stateOf(node.permission)}
          locked={locked}
          onChange={(v) => set(node.permission!, v)}
        />
      ) : (
        <Typography
          variant="subtitle1"
          component="h3"
          className="permission-group"
        >
          {node.title}
        </Typography>
      )}
      {node.children.map((child) => renderMenu(child, depth + 1))}
    </div>
  );

  const save = async () => {
    setBusy(true);
    setProblem(null);
    try {
      const overrides = Object.fromEntries(
        data.data!.permissions.map((p) => [p.key, stateOf(p.key)]),
      );
      const result = await usersApi.setPermissions(id, overrides);
      queryClient.setQueryData(['user-permissions', id], result);
      setChanges({});
      setSaved(true);
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Button component={Link} to="/users" size="small">
        ← Users
      </Button>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Permissions for {user.data.displayName}
      </Typography>
      <p>
        Roles give the starting permissions. Allow or Deny changes one
        permission for this user only; Deny always wins. Hiding a menu also
        blocks its actions on the server. Changes apply within 15 seconds.
      </p>
      {locked && (
        <Alert severity="info" sx={{ mb: 2 }}>
          This user is a SuperAdmin and always has every permission.
        </Alert>
      )}
      <ProblemAlert problem={problem} />
      {saved && (
        <Alert severity="success" sx={{ mb: 2 }} role="status">
          Permissions saved.
        </Alert>
      )}
      <Paper elevation={0} className="permission-panel">
        <Typography variant="h6" component="h2">
          Menus
        </Typography>
        {data.data.menus.map((node) => renderMenu(node, 0))}
      </Paper>
      <Paper elevation={0} className="permission-panel">
        <Typography variant="h6" component="h2">
          Other actions
        </Typography>
        {others.map((p) => (
          <PermissionRow
            key={p.key}
            label={p.description}
            detail={`${p.group}`}
            permission={p}
            state={stateOf(p.key)}
            locked={locked}
            onChange={(v) => set(p.key, v)}
          />
        ))}
      </Paper>
      <Stack direction="row" spacing={2}>
        <Button
          variant="contained"
          disabled={locked || busy || Object.keys(changes).length === 0}
          onClick={() => void save()}
        >
          {busy ? 'Saving…' : 'Save permissions'}
        </Button>
        <Button
          disabled={Object.keys(changes).length === 0}
          onClick={() => setChanges({})}
        >
          Undo changes
        </Button>
      </Stack>
    </>
  );
}
