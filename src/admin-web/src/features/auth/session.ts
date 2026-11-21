import axios, {
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios';
import { apiClient } from '../../api/client';

export type SessionUser = {
  id: string;
  email: string;
  displayName: string;
  mustChangePassword: boolean;
  roles: string[];
  permissions: string[];
};

export type AuthResponse = {
  accessToken: string;
  expiresAt: string;
  user: SessionUser;
};

export type MenuNode = {
  key: string;
  title: string;
  path: string | null;
  icon: string | null;
  children: MenuNode[];
};

/** Required by the API on cookie-authenticated calls (refresh, logout). */
const csrf = { 'X-CSRF-Token': '1' };

// The access token lives only in memory. A page reload restores it from the refresh cookie.
let accessToken: string | null = null;
let inFlight: Promise<AuthResponse | null> | null = null;
const listeners = new Set<(response: AuthResponse | null) => void>();

export const tokenStore = {
  get: () => accessToken,
  set: (response: AuthResponse | null) => {
    accessToken = response?.accessToken ?? null;
    listeners.forEach((listener) => listener(response));
  },
  /** Called with the new session after every sign-in/refresh, or null when the session ends. */
  subscribe: (listener: (response: AuthResponse | null) => void) => {
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  },
};

async function callRefresh(client: AxiosInstance) {
  try {
    const { data } = await client.post<AuthResponse>(
      '/api/auth/refresh',
      null,
      { headers: csrf, skipAuthRetry: true } as never,
    );
    return data;
  } catch (error) {
    // 409 means another request just rotated the token; the cookie now holds the new one.
    if (axios.isAxiosError(error) && error.response?.status === 409) {
      await new Promise((r) => setTimeout(r, 300));
      const { data } = await client.post<AuthResponse>(
        '/api/auth/refresh',
        null,
        { headers: csrf, skipAuthRetry: true } as never,
      );
      return data;
    }
    throw error;
  }
}

/**
 * Gets a new access token from the refresh cookie. Calls are shared within the tab and
 * serialized across tabs with the Web Locks API, so two tabs never send the same refresh token.
 */
export function refreshSession(
  client: AxiosInstance = apiClient,
): Promise<AuthResponse | null> {
  inFlight ??= (async () => {
    try {
      const run = () => callRefresh(client);
      const data =
        typeof navigator !== 'undefined' && navigator.locks
          ? await navigator.locks.request('spareparts-refresh', run)
          : await run();
      tokenStore.set(data);
      return data;
    } catch {
      tokenStore.set(null);
      return null;
    } finally {
      inFlight = null;
    }
  })();
  return inFlight;
}

export const authApi = {
  login: async (email: string, password: string) =>
    (
      await apiClient.post<AuthResponse>(
        '/api/auth/login',
        { email, password },
        { skipAuthRetry: true } as never,
      )
    ).data,
  logout: async () => {
    await apiClient.post('/api/auth/logout', null, {
      headers: csrf,
      skipAuthRetry: true,
    } as never);
  },
  changePassword: async (currentPassword: string, newPassword: string) =>
    (
      await apiClient.post<AuthResponse>('/api/auth/change-password', {
        currentPassword,
        newPassword,
      })
    ).data,
  menus: async () => (await apiClient.get<MenuNode[]>('/api/auth/menus')).data,
};

type RetryConfig = InternalAxiosRequestConfig & {
  skipAuthRetry?: boolean;
  authRetried?: boolean;
};

/** Adds the bearer token and, on a 401, refreshes once and retries the request. */
export function installAuth(client: AxiosInstance) {
  client.interceptors.request.use((config) => {
    const token = tokenStore.get();
    if (token) config.headers.set('Authorization', `Bearer ${token}`);
    return config;
  });
  client.interceptors.response.use(undefined, async (error: unknown) => {
    if (!axios.isAxiosError(error) || error.response?.status !== 401)
      throw error;
    const config = error.config as RetryConfig | undefined;
    if (!config || config.skipAuthRetry || config.authRetried) throw error;
    const session = await refreshSession(client);
    if (!session) throw error;
    config.authRetried = true;
    return client.request(config);
  });
}

installAuth(apiClient);
