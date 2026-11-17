import { apiClient } from '../../api/client';

export type Paged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};
export type RoleRef = { id: string; name: string };

export type UserSummary = {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  isLockedOut: boolean;
  isDeleted: boolean;
  roles: string[];
  lastLoginAt: string | null;
};

export type User = {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  isLockedOut: boolean;
  isDeleted: boolean;
  mustChangePassword: boolean;
  roles: RoleRef[];
  lastLoginAt: string | null;
  updatedAt: string;
  version: number;
};

export type OverrideState = 'Inherit' | 'Allow' | 'Deny';
export type PermissionState = {
  key: string;
  group: string;
  description: string;
  fromRoles: boolean;
  override: OverrideState;
  effective: boolean;
};
export type PermissionMenu = {
  key: string;
  title: string;
  permission: string | null;
  children: PermissionMenu[];
};
export type UserPermissions = {
  userId: string;
  isSuperAdmin: boolean;
  menus: PermissionMenu[];
  permissions: PermissionState[];
};

export type Role = {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissionsLocked: boolean;
  userCount: number;
  permissions: string[];
  version: number;
};
export type PermissionDefinition = {
  key: string;
  group: string;
  description: string;
};

// Enums travel as numbers: Inherit 0, Allow 1, Deny 2.
const toState = (value: unknown): OverrideState =>
  value === 1 || value === 'Allow'
    ? 'Allow'
    : value === 2 || value === 'Deny'
      ? 'Deny'
      : 'Inherit';
const fromState = (state: OverrideState) =>
  state === 'Allow' ? 1 : state === 'Deny' ? 2 : 0;
const normalize = (data: UserPermissions): UserPermissions => ({
  ...data,
  permissions: data.permissions.map((p) => ({
    ...p,
    override: toState(p.override),
  })),
});

export const usersApi = {
  list: async (params: {
    q?: string;
    includeDeleted?: boolean;
    page?: number;
  }) =>
    (
      await apiClient.get<Paged<UserSummary>>('/api/users', {
        params: { pageSize: 50, ...params, q: params.q || undefined },
      })
    ).data,
  get: async (id: string) =>
    (await apiClient.get<User>(`/api/users/${id}`)).data,
  create: async (input: {
    email: string;
    displayName: string;
    temporaryPassword: string;
    roleIds: string[];
  }) => (await apiClient.post<User>('/api/users', input)).data,
  update: async (
    id: string,
    input: {
      email: string;
      displayName: string;
      isActive: boolean;
      version: number;
    },
  ) => (await apiClient.put<User>(`/api/users/${id}`, input)).data,
  remove: async (id: string) => {
    await apiClient.delete(`/api/users/${id}`);
  },
  restore: async (id: string) =>
    (await apiClient.post<User>(`/api/users/${id}/restore`)).data,
  resetPassword: async (id: string, temporaryPassword: string) => {
    await apiClient.post(`/api/users/${id}/reset-password`, {
      temporaryPassword,
    });
  },
  unlock: async (id: string) =>
    (await apiClient.post<User>(`/api/users/${id}/unlock`)).data,
  setRoles: async (id: string, roleIds: string[]) =>
    (await apiClient.put<User>(`/api/users/${id}/roles`, { roleIds })).data,
  roleOptions: async () =>
    (await apiClient.get<RoleRef[]>('/api/roles/options')).data,
  permissions: async (id: string) =>
    normalize(
      (await apiClient.get<UserPermissions>(`/api/users/${id}/permissions`))
        .data,
    ),
  setPermissions: async (
    id: string,
    overrides: Record<string, OverrideState>,
  ) =>
    normalize(
      (
        await apiClient.put<UserPermissions>(`/api/users/${id}/permissions`, {
          overrides: Object.entries(overrides).map(([key, state]) => ({
            key,
            state: fromState(state),
          })),
        })
      ).data,
    ),
  roles: async () => (await apiClient.get<Role[]>('/api/roles')).data,
  permissionCatalog: async () =>
    (await apiClient.get<PermissionDefinition[]>('/api/permissions')).data,
  createRole: async (input: {
    name: string;
    description: string | null;
    permissions: string[];
  }) => (await apiClient.post<Role>('/api/roles', input)).data,
  updateRole: async (
    id: string,
    input: {
      name: string;
      description: string | null;
      permissions: string[];
      version: number;
    },
  ) => (await apiClient.put<Role>(`/api/roles/${id}`, input)).data,
  deleteRole: async (id: string) => {
    await apiClient.delete(`/api/roles/${id}`);
  },
};

export const shopDateTime = (iso: string | null) =>
  iso
    ? new Intl.DateTimeFormat('en-NZ', {
        dateStyle: 'medium',
        timeStyle: 'short',
        timeZone: 'Pacific/Auckland',
      }).format(new Date(iso))
    : 'Never';

/** Effective access for one key given the role default and the chosen override. Deny wins. */
export const effectiveAccess = (fromRoles: boolean, state: OverrideState) =>
  state === 'Deny' ? false : state === 'Allow' ? true : fromRoles;
