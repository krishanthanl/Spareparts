import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  authApi,
  refreshSession,
  tokenStore,
  type AuthResponse,
} from './session';
import { AuthContext, type AuthContextValue, type AuthState } from './useAuth';

export function AuthProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient();
  const [state, setState] = useState<AuthState>({ status: 'loading' });
  const [expiresAt, setExpiresAt] = useState<number | null>(null);

  const apply = useCallback(async (response: AuthResponse | null) => {
    if (!response) {
      setState({ status: 'signedOut' });
      setExpiresAt(null);
      return;
    }
    setExpiresAt(Date.parse(response.expiresAt));
    const menus = response.user.mustChangePassword
      ? []
      : await authApi.menus().catch(() => []);
    setState({ status: 'signedIn', user: response.user, menus });
  }, []);

  // Every refresh (including ones triggered by a 401 elsewhere) updates who is signed in.
  useEffect(
    () =>
      tokenStore.subscribe((response) => {
        if (!response) {
          setState({ status: 'signedOut' });
          setExpiresAt(null);
          queryClient.clear();
        } else setExpiresAt(Date.parse(response.expiresAt));
      }),
    [queryClient],
  );

  // On load, try to restore the session from the refresh cookie.
  useEffect(() => {
    let cancelled = false;
    void refreshSession().then((response) => {
      if (!cancelled) void apply(response);
    });
    return () => {
      cancelled = true;
    };
  }, [apply]);

  // Refresh about a minute before the access token expires.
  useEffect(() => {
    if (expiresAt === null) return;
    const wait = Math.max(5000, expiresAt - Date.now() - 60000);
    const timer = setTimeout(() => void refreshSession(), wait);
    return () => clearTimeout(timer);
  }, [expiresAt]);

  const value = useMemo<AuthContextValue>(() => {
    const permissions =
      state.status === 'signedIn' ? new Set(state.user.permissions) : null;
    return {
      state,
      login: async (email, password) => {
        const response = await authApi.login(email, password);
        tokenStore.set(response);
        await apply(response);
      },
      logout: async () => {
        await authApi.logout().catch(() => undefined);
        tokenStore.set(null);
      },
      changePassword: async (current, next) => {
        const response = await authApi.changePassword(current, next);
        tokenStore.set(response);
        await apply(response);
      },
      can: (permission) => permissions?.has(permission) ?? false,
    };
  }, [state, apply]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
